using System.Diagnostics;
using System.Drawing;
using SplitDeck.Core;
using Forms=System.Windows.Forms;

namespace SplitDeck.Windows;

internal sealed record InteractionCaseMetrics(int CaseIndex,string Case,int TargetIndex,string DeviceName,int TimerSamples,double MeanTimerGapMs,double P95TimerGapMs,double MaximumTimerGapMs,int GapsOver100Ms,int GapsOver250Ms,int MenuOpened,int MenuClosed,int MenuItemInvocations,int ScrollOperations,int WindowMoves,int AnimationRequests,int Paints,bool Completed);
internal sealed record InteractionFixtureEvent(double ElapsedMilliseconds,int WindowIndex,int TargetIndex,string DeviceName,string Operation);
internal sealed record InteractionFixtureResult(string Scope,bool NativeInputTested,bool Completed,bool Canceled,double ElapsedSeconds,int CompletedCases,IReadOnlyList<InteractionCaseMetrics> Cases,IReadOnlyList<InteractionFixtureEvent> Events,string? Error);

/// <summary>
/// Bounded composition workload using only windows and controls owned by this process.
/// Programmatic menu/scroll operations exercise repaint and capture work; they do not
/// establish the latency or correctness of real hardware mouse input.
/// </summary>
internal sealed class InteractionFixture:IAsyncDisposable
{
 private static readonly string[] CaseNames=["animation","context menus","text scrolling","window movement","combined"];
 private readonly MonitorInfo[] monitors;private readonly TimeSpan duration;private readonly Thread thread;
 private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
 private readonly TaskCompletionSource<InteractionFixtureResult> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
 private readonly CancellationTokenRegistration cancellation;private int stopRequested;
 internal Task Ready=>ready.Task;
 internal Task<InteractionFixtureResult> Completion=>completion.Task;

 private InteractionFixture(IReadOnlyList<MonitorInfo> monitors,TimeSpan duration,CancellationToken cancellationToken)
 {
  if(monitors.Count is <1 or >4||monitors.Any(m=>m.Width<160||m.Height<160))throw new ArgumentException("The interaction fixture requires one to four visible display regions of at least 160 pixels.",nameof(monitors));
  if(duration<TimeSpan.FromSeconds(5)||duration>TimeSpan.FromSeconds(60))throw new ArgumentOutOfRangeException(nameof(duration),"The workload must last five to sixty seconds.");
  this.monitors=monitors.ToArray();this.duration=duration;
  cancellation=cancellationToken.Register(()=>Interlocked.Exchange(ref stopRequested,1));
  thread=new Thread(Run){IsBackground=true,Name="SplitDeck own-window interaction fixture"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
 }
 internal static InteractionFixture Start(IReadOnlyList<MonitorInfo> monitors,TimeSpan? duration=null,CancellationToken cancellationToken=default)=>new(monitors,duration??TimeSpan.FromSeconds(20),cancellationToken);

 private void Run()
 {
  var watch=Stopwatch.StartNew();var windows=new List<FixtureWindow>();var events=new List<InteractionFixtureEvent>();
  var metrics=Enumerable.Range(0,monitors.Length).Select(target=>Enumerable.Range(0,CaseNames.Length).Select(index=>new Counters(index,target)).ToArray()).ToArray();
  Exception? error=null;bool finished=false;int previousCase=0;long lastTick=0;double nextMenu=0,nextScroll=0,nextMove=0,nextAnimation=0;
  using var context=new Forms.ApplicationContext();using var timer=new Forms.Timer{Interval=16};
  int CaseAt(double seconds)=>Math.Min(CaseNames.Length-1,(int)(seconds/duration.TotalSeconds*CaseNames.Length));
  void Record(FixtureWindow window,string operation){if(events.Count<512)events.Add(new(watch.Elapsed.TotalMilliseconds,window.WindowIndex,window.TargetIndex,monitors[window.TargetIndex].DeviceName,operation));}
  try{
   for(int index=0;index<monitors.Length;index++){
    FixtureWindow? window=null;
    window=new FixtureWindow(index,monitors,index,()=>{if(watch.Elapsed<duration)Interlocked.Exchange(ref stopRequested,1);context.ExitThread();},()=>{if(window is not null)metrics[window.TargetIndex][CaseAt(watch.Elapsed.TotalSeconds)].Paints++;});
    windows.Add(window);
    window.Popup.Opened+=(_,_)=>{metrics[window.TargetIndex][CaseAt(watch.Elapsed.TotalSeconds)].MenuOpened++;Record(window,"menu opened");};
    window.Popup.Closed+=(_,_)=>{metrics[window.TargetIndex][CaseAt(watch.Elapsed.TotalSeconds)].MenuClosed++;Record(window,"menu closed");};
    window.MenuAction.Click+=(_,_)=>{metrics[window.TargetIndex][CaseAt(watch.Elapsed.TotalSeconds)].MenuItemInvocations++;Record(window,"own menu item invoked");};
    window.Show();
   }
   watch.Restart();lastTick=Stopwatch.GetTimestamp();foreach(var window in windows)Record(window,"case: "+CaseNames[0]);
   timer.Tick+=(_,_)=>{
    try{
     long now=Stopwatch.GetTimestamp();double seconds=watch.Elapsed.TotalSeconds;
     double gap=(now-lastTick)*1000.0/Stopwatch.Frequency;lastTick=now;
     foreach(var target in metrics)target[previousCase].TimerGaps.Add(gap);
     if(Volatile.Read(ref stopRequested)!=0||seconds>=duration.TotalSeconds){finished=seconds>=duration.TotalSeconds&&Volatile.Read(ref stopRequested)==0;context.ExitThread();return;}
     int caseIndex=CaseAt(seconds);
     if(caseIndex!=previousCase){foreach(var window in windows){window.Popup.Close();window.SetCase(CaseNames[caseIndex]);Record(window,"case: "+CaseNames[caseIndex]);}nextMenu=nextScroll=nextMove=seconds;previousCase=caseIndex;}
     bool menus=caseIndex is 1 or 4,scroll=caseIndex is 2 or 4,movement=caseIndex is 3 or 4;
     // Open and close separately so the compositor sees actual popup surfaces.
     if(menus&&seconds>=nextMenu){
      foreach(var window in windows){if(window.Popup.Visible){window.Popup.Close();}else{window.Popup.Show(window,new Point(24,75));window.MenuAction.PerformClick();}}
      nextMenu=seconds+0.35;
     }
     if(scroll&&seconds>=nextScroll){foreach(var window in windows){window.ScrollText();metrics[window.TargetIndex][caseIndex].ScrollOperations++;}nextScroll=seconds+0.08;}
     if(movement&&seconds>=nextMove){
      foreach(var window in windows)if(window.MoveTo((window.TargetIndex+1)%monitors.Length)){metrics[window.TargetIndex][caseIndex].WindowMoves++;Record(window,"own window moved");}
      nextMove=seconds+0.9;
     }
     if(seconds>=nextAnimation){foreach(var window in windows){window.Animate();metrics[window.TargetIndex][caseIndex].AnimationRequests++;}nextAnimation=seconds+1.0/30;}
    }catch(Exception e){error=e;context.ExitThread();}
   };
   timer.Start();ready.TrySetResult();Forms.Application.Run(context);
  }catch(Exception e){error=e;ready.TrySetException(e);}
  finally{
   timer.Stop();watch.Stop();
   foreach(var window in windows)try{window.Popup.Close();window.Close();window.Dispose();}catch(Exception e){error??=e;}
   bool CaseCompleted(int index)=>watch.Elapsed.TotalSeconds>=(index+1)*(duration.TotalSeconds/CaseNames.Length)&&metrics.All(target=>target[index].TimerGaps.Count>0&&target[index].AnimationRequests>0);
   int completedCases=Enumerable.Range(0,CaseNames.Length).Count(CaseCompleted);
   var results=metrics.SelectMany(target=>target.Select(counter=>counter.ToResult(monitors[counter.TargetIndex].DeviceName,CaseCompleted(counter.CaseIndex)))).ToArray();
   completion.TrySetResult(new("Own-process WinForms controls and popup surfaces only. Metrics are attributed to the window's actual target region when each operation occurs. Programmatic actions do not measure native mouse input or a third-party application's response.",false,finished&&completedCases==CaseNames.Length&&error is null,Volatile.Read(ref stopRequested)!=0,watch.Elapsed.TotalSeconds,completedCases,results,events.ToArray(),error?.ToString()));
   if(!ready.Task.IsCompleted)ready.TrySetCanceled();
  }
 }

 public async ValueTask DisposeAsync()
 {
  Interlocked.Exchange(ref stopRequested,1);
  try{await Completion.WaitAsync(TimeSpan.FromSeconds(5));}
  finally{cancellation.Dispose();}
 }

 private sealed class Counters(int caseIndex,int targetIndex)
 {
  internal int CaseIndex{get;}=caseIndex;internal int TargetIndex{get;}=targetIndex;
  internal readonly List<double> TimerGaps=[];
  internal int MenuOpened,MenuClosed,MenuItemInvocations,ScrollOperations,WindowMoves,AnimationRequests,Paints;
  internal InteractionCaseMetrics ToResult(string deviceName,bool completed)
  {
   var gaps=TimerGaps.Order().ToArray();
   return new(CaseIndex,CaseNames[CaseIndex],TargetIndex,deviceName,gaps.Length,gaps.Length==0?0:gaps.Average(),gaps.Length==0?0:gaps[Math.Clamp((int)Math.Ceiling(gaps.Length*0.95)-1,0,gaps.Length-1)],gaps.Length==0?0:gaps[^1],gaps.Count(gap=>gap>100),gaps.Count(gap=>gap>250),MenuOpened,MenuClosed,MenuItemInvocations,ScrollOperations,WindowMoves,AnimationRequests,Paints,completed);
  }
 }

 private sealed class FixtureWindow:Forms.Form
 {
  private readonly MonitorInfo[] monitors;private readonly Forms.Label label;private readonly Forms.TextBox text;private readonly Marker marker;private int line;
  internal int WindowIndex{get;}internal int TargetIndex{get;private set;}
  internal Forms.ContextMenuStrip Popup{get;}=new(){AutoClose=false};
  internal Forms.ToolStripMenuItem MenuAction{get;}=new("Diagnostic menu action");
  protected override bool ShowWithoutActivation=>true;
  protected override Forms.CreateParams CreateParams{get{var parameters=base.CreateParams;parameters.ExStyle|=0x08000080;return parameters;}}
  internal FixtureWindow(int windowIndex,MonitorInfo[] monitors,int targetIndex,Action closed,Action painted)
  {
   this.monitors=monitors;WindowIndex=windowIndex;Text="SplitDeck interaction fixture — "+(windowIndex+1);StartPosition=Forms.FormStartPosition.Manual;ShowInTaskbar=false;
   BackColor=Color.FromArgb(27,34,30);ForeColor=Color.White;FormBorderStyle=Forms.FormBorderStyle.SizableToolWindow;MinimumSize=new Size(160,160);
   label=new Forms.Label{Dock=Forms.DockStyle.Top,Height=42,ForeColor=Color.White,Padding=new Forms.Padding(8),Text="Own-app workload: animation"};
   text=new Forms.TextBox{Dock=Forms.DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=Forms.ScrollBars.Both,WordWrap=false,Text=string.Join(Environment.NewLine,Enumerable.Range(1,240).Select(index=>$"Fixture {windowIndex+1} • line {index:000} — menu / scroll / movement"))};
   marker=new Marker(painted){Dock=Forms.DockStyle.Top,Height=50};
   Controls.Add(text);Controls.Add(marker);Controls.Add(label);
   Popup.Items.AddRange([MenuAction,new Forms.ToolStripSeparator(),new Forms.ToolStripMenuItem("Owned test popup")]);
   text.ContextMenuStrip=Popup;FormClosed+=(_,_)=>closed();MoveTo(targetIndex);
  }
  internal void SetCase(string name)=>label.Text="Own-app workload: "+name;
  internal void ScrollText(){line=(line+7)%230;int character=text.GetFirstCharIndexFromLine(line);if(character>=0){text.Select(character,0);text.ScrollToCaret();}}
  internal void Animate()=>marker.Animate();
  internal bool MoveTo(int targetIndex)
  {
   Popup.Close();TargetIndex=targetIndex;var target=monitors[targetIndex];
   int width=Math.Max(160,Math.Min(480,target.Width-40)),height=Math.Max(160,Math.Min(330,target.Height-60));
   int x=target.X+Math.Max(0,(target.Width-width)/2),y=target.Y+Math.Max(0,(target.Height-height)/2);
   var next=new Rectangle(x,y,width,height);bool moved=Bounds!=next;Bounds=next;return moved;
  }
  protected override void Dispose(bool disposing){if(disposing)Popup.Dispose();base.Dispose(disposing);}
 }

 private sealed class Marker(Action painted):Forms.Control
 {
  private readonly SolidBrush brush=new(Color.FromArgb(184,235,121));private int frame;
  internal Marker():this(()=>{}){}
  internal void Animate(){frame++;Invalidate();}
  protected override void OnPaint(Forms.PaintEventArgs e){base.OnPaint(e);painted();e.Graphics.Clear(Color.FromArgb(39,49,42));int maximum=Math.Max(1,ClientSize.Width-28);e.Graphics.FillRectangle(brush,frame*7%maximum,10,24,28);}
  protected override void Dispose(bool disposing){if(disposing)brush.Dispose();base.Dispose(disposing);}
 }
}
