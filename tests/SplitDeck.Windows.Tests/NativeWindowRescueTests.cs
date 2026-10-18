using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using SplitDeck.Core;
using SplitDeck.Windows;
using Forms=System.Windows.Forms;

internal static class NativeWindowRescueTests
{
 internal static async Task ShowStates()
 {
  var monitor=new DisplayService().Detect().First(m=>!m.IsVirtual);string folder=Path.Combine(Path.GetTempPath(),"SplitDeck-owned-rescue-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string ready=Path.Combine(folder,"ready.json");
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};if(Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(NativeWindowRescueTests).Assembly.Location);start.ArgumentList.Add("--window-rescue-fixture");start.ArgumentList.Add(ready);
  using var child=Process.Start(start)??throw new Exception("Could not start owned window fixture.");
  var all=new List<WindowPlacementSnapshot>();
  try{
   var timeout=Stopwatch.StartNew();while(!File.Exists(ready)&&timeout.Elapsed<TimeSpan.FromSeconds(8)&&!child.HasExited)await Task.Delay(30);
   Check(File.Exists(ready),"own child fixture became ready");
   long[] handles=JsonSerializer.Deserialize<long[]>(await File.ReadAllTextAsync(ready))!;
   all=WindowRescue.Capture(monitor);var own=all.Where(s=>s.ProcessId==child.Id&&handles.Contains(s.Handle)).ToList();WindowRescue.Release(all.Except(own).ToArray());
   Check(own.Count==4,"captured normal, maximized, minimized and owned modal fixture windows");
   Check(!IsWindowEnabled((nint)handles[0]),"real modal dialog disables its owner before migration");
   bool[] minimized=handles.Select(h=>IsIconic((nint)h)).ToArray(),maximized=handles.Select(h=>IsZoomed((nint)h)).ToArray();nint foreground=GetForegroundWindow();
   int half=monitor.Width/2;
   var left=monitor with{Width=half};var right=monitor with{X=monitor.X+half,Width=monitor.Width-half};
   var moved=await WindowRescue.MoveToVirtualsAsync(own,monitor,[left,right]);
   Check(moved.Queued==4&&moved.Confirmed==4&&moved.Issues.Count==0,"all own fixture placements confirmed: "+JsonSerializer.Serialize(moved));
   AssertVisibleBounds(own,monitor,[left,right]);
   Check(GetForegroundWindow()==foreground,"migration did not change foreground window");
   Check(!IsWindowEnabled((nint)handles[0]),"migration leaves modal owner disabled");
   for(int i=0;i<handles.Length;i++)Check(IsIconic((nint)handles[i])==minimized[i]&&IsZoomed((nint)handles[i])==maximized[i],"migration preserves minimize/maximize state "+i);
   var restored=await WindowRescue.RestoreAsync(own);
   Check(restored.Confirmed==4&&restored.Issues.Count==0,"original own restore bounds confirmed: "+JsonSerializer.Serialize(restored));
   Check(GetForegroundWindow()==foreground,"restoration did not change foreground window");
   Check(!IsWindowEnabled((nint)handles[0]),"restoration leaves modal owner disabled");
   for(int i=0;i<handles.Length;i++)Check(IsIconic((nint)handles[i])==minimized[i]&&IsZoomed((nint)handles[i])==maximized[i],"restoration preserves minimize/maximize state "+i);
  }finally{
   WindowRescue.Release(all);await File.WriteAllTextAsync(ready+".done","");
   try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){child.Kill();await child.WaitForExitAsync();}
   foreach(string file in new[]{ready,ready+".done"})if(File.Exists(file))File.Delete(file);Directory.Delete(folder);
  }
 }
 private static void AssertVisibleBounds(IReadOnlyList<WindowPlacementSnapshot> windows,MonitorInfo original,IReadOnlyList<MonitorInfo> targets)
 {
  nint previous=SetThreadDpiAwarenessContext(-4);
  try{
   PixelRect[] bounds=targets.Select(t=>new PixelRect(t.X,t.Y,t.Width,t.Height)).ToArray();
   var physicalWork=Forms.Screen.FromPoint(new Point(original.X+original.Width/2,original.Y+original.Height/2)).WorkingArea;
   PixelRect[] work=bounds.Select(b=>{var intersection=Rectangle.Intersect(new Rectangle(b.X,b.Y,b.Width,b.Height),physicalWork);return new PixelRect(intersection.X,intersection.Y,intersection.Width,intersection.Height);}).ToArray();
   foreach(var saved in windows){var handle=(nint)saved.Handle;if(IsIconic(handle))continue;
    var destination=WindowRescuePolicy.Destination(saved.NormalScreenBounds,new(original.X,original.Y,original.Width,original.Height),bounds,work);var target=IsZoomed(handle)?work[destination.Index]:destination.Bounds;
    Check(GetWindowRect(handle,out var actual),"read actual visible fixture window bounds");
    Check(Math.Abs((long)actual.Left-target.X)<=12&&Math.Abs((long)actual.Top-target.Y)<=12&&Math.Abs((long)(actual.Right-actual.Left)-target.Width)<=24&&Math.Abs((long)(actual.Bottom-actual.Top)-target.Height)<=24,"actual normal/maximized fixture is visible on selected destination, not merely its saved restore rectangle");
   }
  }finally{if(previous!=0)SetThreadDpiAwarenessContext(previous);}
 }
 internal static Task<int> RunFixtureAsync(string ready)
 {
  var completion=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
  var thread=new Thread(()=>{
   var windows=new List<Forms.Form>();using var context=new Forms.ApplicationContext();using var timer=new Forms.Timer{Interval=100};var watch=Stopwatch.StartNew();
   try{
    var monitor=new DisplayService().Detect().First(m=>!m.IsVirtual);
    for(int index=0;index<3;index++){var form=new Forms.Form{Text="SplitDeck owned rescue fixture "+index,StartPosition=Forms.FormStartPosition.Manual,Bounds=new Rectangle(monitor.X+50+index*60,monitor.Y+80+index*30,500,350)};windows.Add(form);form.Show();}
    windows[1].WindowState=Forms.FormWindowState.Maximized;windows[2].WindowState=Forms.FormWindowState.Minimized;
    var dialog=new Forms.Form{Text="SplitDeck owned modal fixture",StartPosition=Forms.FormStartPosition.Manual,Bounds=new Rectangle(monitor.X+140,monitor.Y+150,380,260),FormBorderStyle=Forms.FormBorderStyle.FixedDialog};windows.Add(dialog);
    bool wrote=false,shown=false;timer.Tick+=(_,_)=>{
     if(!shown){shown=true;dialog.ShowDialog(windows[0]);return;}
     if(!wrote&&watch.ElapsedMilliseconds>500){File.WriteAllText(ready,JsonSerializer.Serialize(windows.Select(f=>f.Handle.ToInt64()).ToArray()));wrote=true;}
     if(File.Exists(ready+".done")||watch.Elapsed>TimeSpan.FromSeconds(15)){dialog.Close();context.ExitThread();}
    };timer.Start();Forms.Application.Run(context);completion.TrySetResult(0);
   }catch(Exception e){completion.TrySetException(e);}finally{timer.Stop();foreach(var form in windows)form.Dispose();}
  }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 [DllImport("user32.dll")]private static extern bool IsIconic(nint window);
 [DllImport("user32.dll")]private static extern bool IsZoomed(nint window);
 [DllImport("user32.dll")]private static extern bool IsWindowEnabled(nint window);
 [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
 [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out Rect rect);
 [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint context);
}
