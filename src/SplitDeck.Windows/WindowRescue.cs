using SplitDeck.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SplitDeck.Windows;

internal sealed record WindowPlacementSnapshot(long Handle,uint ProcessId,long ProcessStartedUtcTicks,string ClassName,long Token,WindowPlacementValue Placement,PixelRect NormalScreenBounds,PixelRect SourceMonitor,PixelRect SourceWorkArea,long OwnerHandle=0);
internal sealed record WindowPlacementValue(uint Flags,uint ShowCommand,int MinX,int MinY,int MaxX,int MaxY,PixelRect NormalPosition);
internal sealed record WindowRescueIssue(long Handle,uint ProcessId,string Detail);
internal sealed record WindowRescueResult(int Considered,int Queued,int Confirmed,int Skipped,IReadOnlyList<WindowRescueIssue> Issues);
internal sealed record WindowRescueDestination(int Index,PixelRect Bounds);

internal static class WindowRescuePolicy
{
 internal static IReadOnlyList<long> Family(IReadOnlyList<(long Handle,long Owner)> candidates)
 {
  var selected=candidates.Where(c=>c.Owner==0).Select(c=>c.Handle).Distinct().ToList();var seen=selected.ToHashSet();
  // EnumWindows returns z-order, so an owned dialog can precede its owner.
  // Resolve only chains rooted at an eligible captured app, never orphan tools.
  for(int pass=0;pass<16;pass++){int before=selected.Count;foreach(var candidate in candidates)if(candidate.Owner!=0&&seen.Contains(candidate.Owner)&&seen.Add(candidate.Handle))selected.Add(candidate.Handle);if(selected.Count==before)break;}
  return selected;
 }
 internal static bool ModalTool(bool owned,bool ownerDisabled,bool dialogStyle)=>owned&&ownerDisabled&&dialogStyle;
 internal static bool Eligible(bool visible,bool cloaked,bool owned,bool tool,bool noActivate,bool child,bool ownProcess,bool shell)=>visible&&!cloaked&&!owned&&!tool&&!noActivate&&!child&&!ownProcess&&!shell;
 internal static PixelRect ToScreen(PixelRect workspace,PixelRect monitor,PixelRect workArea)=>workspace with{X=checked(workspace.X+workArea.X-monitor.X),Y=checked(workspace.Y+workArea.Y-monitor.Y)};
 internal static PixelRect ToWorkspace(PixelRect screen,PixelRect monitor,PixelRect workArea)=>screen with{X=checked(screen.X-workArea.X+monitor.X),Y=checked(screen.Y-workArea.Y+monitor.Y)};
 internal static WindowRescueDestination Destination(PixelRect originalWindow,PixelRect source,IReadOnlyList<PixelRect> targets,IReadOnlyList<PixelRect> workAreas)
 {
  if(source.Width<=0||source.Height<=0||originalWindow.Width<=0||originalWindow.Height<=0||targets.Count==0||targets.Count!=workAreas.Count||targets.Any(t=>t.Width<=0||t.Height<=0)||workAreas.Any(t=>t.Width<=0||t.Height<=0))throw new ArgumentException("Valid source and destination work areas are required.");
  double nx=Math.Clamp((originalWindow.X+originalWindow.Width/2.0-source.X)/source.Width,0,1),ny=Math.Clamp((originalWindow.Y+originalWindow.Height/2.0-source.Y)/source.Height,0,1);
  int left=targets.Min(t=>t.X),top=targets.Min(t=>t.Y);long right=targets.Max(t=>(long)t.X+t.Width),bottom=targets.Max(t=>(long)t.Y+t.Height);
  double x=left+nx*(right-left),y=top+ny*(bottom-top);
  double Distance(PixelRect t){double dx=Math.Max(t.X-x,Math.Max(0,x-((long)t.X+t.Width))),dy=Math.Max(t.Y-y,Math.Max(0,y-((long)t.Y+t.Height)));return dx*dx+dy*dy;}
  int index=Enumerable.Range(0,targets.Count).OrderBy(i=>Distance(targets[i])).First();var work=workAreas[index];
  int width=Math.Min(originalWindow.Width,work.Width),height=Math.Min(originalWindow.Height,work.Height);
  int wx=(int)Math.Clamp(Math.Round(x-width/2.0),work.X,(long)work.X+work.Width-width),wy=(int)Math.Clamp(Math.Round(y-height/2.0),work.Y,(long)work.Y+work.Height-height);
  return new(index,new(wx,wy,width,height));
 }
}
internal static class WindowRescue
{
 private const string IdentityProperty="SplitDeck.Rescue.Identity",MovedProperty="SplitDeck.Rescue.Moved";
 private const uint AsyncPlacement=4,ShowWithoutActivation=8,AsyncPosition=0x4000,NoActivate=0x10,NoZOrder=4,NoOwnerZOrder=0x200;
 internal static List<WindowPlacementSnapshot> Capture(MonitorInfo selectedPhysical)
 {
  using var dpi=new DpiScope();var candidates=new List<WindowPlacementSnapshot>();var started=Stopwatch.StartNew();var processStarts=new Dictionary<uint,long?>();
  EnumWindows((handle,_)=>{
   if(candidates.Count>=256||started.Elapsed>TimeSpan.FromSeconds(2))return false;
   try{
    GetWindowThreadProcessId(handle,out uint pid);var className=ClassName(handle);long style=GetWindowLongPtr(handle,-16).ToInt64(),extended=GetWindowLongPtr(handle,-20).ToInt64();
    int cloaked=0;DwmGetWindowAttribute(handle,14,out cloaked,4);
    bool shell=handle==GetShellWindow()||handle==GetDesktopWindow()||className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW"||className.StartsWith("SplitDeckHost_",StringComparison.Ordinal);
    nint owner=GetWindow(handle,4);
    bool modalTool=WindowRescuePolicy.ModalTool(owner!=0,owner!=0&&!IsWindowEnabled(owner),className=="#32770"||(extended&1)!=0);
    // Owned candidates are admitted only after their eligible owner family is known.
    if(!WindowRescuePolicy.Eligible(IsWindowVisible(handle),cloaked!=0,false,(extended&0x80)!=0&&!modalTool,(extended&0x08000000)!=0,(style&0x40000000)!=0,pid==Environment.ProcessId,shell))return true;
    if(!TryMonitor(handle,out var monitor)||!string.Equals(monitor.DeviceName,selectedPhysical.DeviceName,StringComparison.OrdinalIgnoreCase))return true;
    var placement=Placement.New();if(!GetWindowPlacement(handle,ref placement))return true;
    var normal=AsRect(placement.NormalPosition);if(normal.Width<=0||normal.Height<=0)return true;
    if(!processStarts.TryGetValue(pid,out var processStarted)){processStarted=ProcessStarted(pid);processStarts.Add(pid,processStarted);}if(processStarted is null)return true;
    int token=RandomNumberGenerator.GetInt32(1,int.MaxValue);
    candidates.Add(new(handle.ToInt64(),pid,processStarted.Value,className,token,placement.Value(),WindowRescuePolicy.ToScreen(normal,AsRect(monitor.Bounds),AsRect(monitor.WorkArea)),AsRect(monitor.Bounds),AsRect(monitor.WorkArea),owner.ToInt64()));
   }catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or OverflowException){}
   return true;
  },0);
  var byHandle=candidates.ToDictionary(candidate=>candidate.Handle);var saved=new List<WindowPlacementSnapshot>();var captured=new HashSet<long>();
  foreach(long handle in WindowRescuePolicy.Family(candidates.Select(c=>(c.Handle,c.OwnerHandle)).ToArray())){
   var candidate=byHandle[handle];if(candidate.OwnerHandle!=0&&!captured.Contains(candidate.OwnerHandle))continue;
   if(!SetProp((nint)handle,IdentityProperty,(nint)candidate.Token))continue;
   saved.Add(candidate);captured.Add(handle);
  }
  return saved;
 }
 internal static Task<WindowRescueResult> MoveToVirtualsAsync(IReadOnlyList<WindowPlacementSnapshot> windows,MonitorInfo originalPhysical,IReadOnlyList<MonitorInfo> virtuals)
 {
  if(virtuals.Count==0)return Task.FromResult(new WindowRescueResult(windows.Count,0,0,windows.Count,[new(0,0,"No virtual destination was available; existing windows were left unchanged.")]));
  var bounds=virtuals.Select(Bounds).ToArray();var work=virtuals.Select(WorkArea).ToArray();var source=Bounds(originalPhysical);
  return ApplyAsync(windows,saved=>{var destination=WindowRescuePolicy.Destination(saved.NormalScreenBounds,source,bounds,work);return new Destination(destination.Bounds,bounds[destination.Index],work[destination.Index]);},false);
 }
 internal static Task<WindowRescueResult> RestoreAsync(IReadOnlyList<WindowPlacementSnapshot> windows)=>ApplyAsync(windows,saved=>{
  using var dpi=new DpiScope();var r=NativeRect(saved.NormalScreenBounds);var monitor=MonitorInformation.New();
  if(!GetMonitorInfo(MonitorFromRect(ref r,2),ref monitor))throw new InvalidOperationException("The original window monitor is unavailable.");
  if(AsRect(monitor.Bounds)!=saved.SourceMonitor)throw new InvalidOperationException("The original window monitor moved or disconnected; saved window bounds were not replayed.");
  return new Destination(saved.NormalScreenBounds,AsRect(monitor.Bounds),AsRect(monitor.WorkArea));
 },true);
 // Remove only our identity properties when preparation is abandoned before migration.
 internal static void Release(IReadOnlyList<WindowPlacementSnapshot> windows){foreach(var saved in windows)if(Matches(saved)){RemoveProp((nint)saved.Handle,IdentityProperty);RemoveProp((nint)saved.Handle,MovedProperty);}}
 private sealed record Destination(PixelRect Normal,PixelRect Monitor,PixelRect WorkArea);
 private sealed record Pending(WindowPlacementSnapshot Saved,Destination Target,bool Minimized,bool Maximized);
 private static async Task<WindowRescueResult> ApplyAsync(IReadOnlyList<WindowPlacementSnapshot> windows,Func<WindowPlacementSnapshot,Destination> destination,bool restoring)
 {
  var issues=new List<WindowRescueIssue>();var pending=new List<Pending>();int skipped=0,queued=0,confirmed=0;
  foreach(var saved in windows.Take(256)){
   try{
    using var dpi=new DpiScope();var handle=(nint)saved.Handle;
    if(!Matches(saved)){skipped++;continue;}
    if(restoring&&GetProp(handle,MovedProperty)!=(nint)saved.Token){RemoveProp(handle,IdentityProperty);skipped++;continue;}
    var target=destination(saved);var placement=Placement.New();if(!GetWindowPlacement(handle,ref placement))throw new InvalidOperationException("Windows could not read the app's current placement.");
    bool minimized=IsIconic(handle),maximized=IsZoomed(handle);
    placement.Flags=(placement.Flags&2)|AsyncPlacement;placement.ShowCommand=ShowWithoutActivation;
    placement.NormalPosition=NativeRect(WindowRescuePolicy.ToWorkspace(target.Normal,target.Monitor,target.WorkArea));placement.MinPosition=new(-1,-1);placement.MaxPosition=new(-1,-1);
    if(!restoring&&!SetProp(handle,MovedProperty,(nint)saved.Token))throw new InvalidOperationException("Windows could not retain the app's restoration identity.");
    if(!SetWindowPlacement(handle,ref placement))throw new InvalidOperationException("Windows rejected the nonactivating placement request.");
    if(!minimized){var visible=maximized?target.WorkArea:target.Normal;if(!SetWindowPos(handle,0,visible.X,visible.Y,visible.Width,visible.Height,AsyncPosition|NoActivate|NoZOrder|NoOwnerZOrder))throw new InvalidOperationException("Windows rejected the nonactivating window move.");}
    queued++;pending.Add(new(saved,target,minimized,maximized));
   }catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or OverflowException){issues.Add(new(saved.Handle,saved.ProcessId,e.Message));}
  }
  // Posted window-manager operations never wait for a hung application's message loop.
  // Verify them for a bounded period and report misses without blocking desktop recovery.
  var deadline=Stopwatch.StartNew();
  while(pending.Count>0&&deadline.Elapsed<TimeSpan.FromSeconds(2)){
   for(int i=pending.Count-1;i>=0;i--){var item=pending[i];using var dpi=new DpiScope();var handle=(nint)item.Saved.Handle;
    if(!Matches(item.Saved)){pending.RemoveAt(i);skipped++;continue;}
    var placement=Placement.New();bool states=IsIconic(handle)==item.Minimized&&IsZoomed(handle)==item.Maximized;
    bool bounds=GetWindowPlacement(handle,ref placement)&&Near(WindowRescuePolicy.ToScreen(AsRect(placement.NormalPosition),item.Target.Monitor,item.Target.WorkArea),item.Target.Normal);
    if(!item.Minimized)bounds=bounds&&GetWindowRect(handle,out var actual)&&Near(AsRect(actual),item.Maximized?item.Target.WorkArea:item.Target.Normal);
    if(states&&bounds){confirmed++;pending.RemoveAt(i);if(restoring){RemoveProp(handle,IdentityProperty);RemoveProp(handle,MovedProperty);}}
   }
   if(pending.Count>0)await Task.Delay(40);
  }
  foreach(var item in pending)issues.Add(new(item.Saved.Handle,item.Saved.ProcessId,"The app did not confirm the requested bounds and unchanged minimize/maximize state within two seconds."));
  return new(windows.Count,queued,confirmed,skipped,issues);
 }
 private static bool Near(PixelRect a,PixelRect b)=>Math.Abs((long)a.X-b.X)<=12&&Math.Abs((long)a.Y-b.Y)<=12&&Math.Abs((long)a.Width-b.Width)<=24&&Math.Abs((long)a.Height-b.Height)<=24;
 private static bool Matches(WindowPlacementSnapshot saved)=>saved.Token>0&&IsWindow((nint)saved.Handle)&&GetProp((nint)saved.Handle,IdentityProperty)==(nint)saved.Token&&GetWindowThreadProcessId((nint)saved.Handle,out uint pid)!=0&&pid==saved.ProcessId&&ClassName((nint)saved.Handle)==saved.ClassName&&ProcessStarted(pid)==saved.ProcessStartedUtcTicks;
 private static long? ProcessStarted(uint id){try{using var process=Process.GetProcessById((int)id);return process.StartTime.ToUniversalTime().Ticks;}catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException){return null;}}
 private static string ClassName(nint handle){var text=new StringBuilder(256);GetClassName(handle,text,text.Capacity);return text.ToString();}
 private static PixelRect Bounds(MonitorInfo monitor)=>new(monitor.X,monitor.Y,monitor.Width,monitor.Height);
 private static PixelRect WorkArea(MonitorInfo monitor){using var dpi=new DpiScope();var bounds=Bounds(monitor);var r=NativeRect(bounds);var info=MonitorInformation.New();if(!GetMonitorInfo(MonitorFromRect(ref r,2),ref info))return bounds;var work=AsRect(info.WorkArea);int x=Math.Max(work.X,bounds.X),y=Math.Max(work.Y,bounds.Y),right=Math.Min(work.X+work.Width,bounds.X+bounds.Width),bottom=Math.Min(work.Y+work.Height,bounds.Y+bounds.Height);return right>x&&bottom>y?new(x,y,right-x,bottom-y):bounds;}
 private static bool TryMonitor(nint window,out MonitorInformation info){info=MonitorInformation.New();return GetMonitorInfo(MonitorFromWindow(window,2),ref info);}
 private static PixelRect AsRect(Rect rect)=>new(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
 private static Rect NativeRect(PixelRect rect)=>new(rect.X,rect.Y,checked(rect.X+rect.Width),checked(rect.Y+rect.Height));
 [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;public Point(int x,int y){X=x;Y=y;}}
 [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;public Rect(int left,int top,int right,int bottom){Left=left;Top=top;Right=right;Bottom=bottom;}}
 [StructLayout(LayoutKind.Sequential)]private struct Placement{
  public uint Length,Flags,ShowCommand;public Point MinPosition,MaxPosition;public Rect NormalPosition;
  internal static Placement New()=>new(){Length=(uint)Marshal.SizeOf<Placement>()};
  internal WindowPlacementValue Value()=>new(Flags,ShowCommand,MinPosition.X,MinPosition.Y,MaxPosition.X,MaxPosition.Y,AsRect(NormalPosition));
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct MonitorInformation{public int Size;public Rect Bounds,WorkArea;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DeviceName;internal static MonitorInformation New()=>new(){Size=Marshal.SizeOf<MonitorInformation>(),DeviceName=""};}
 private sealed class DpiScope:IDisposable{private readonly nint previous=SetThreadDpiAwarenessContext(-4);public DpiScope(){if(previous==0)throw new InvalidOperationException("Windows could not establish physical-pixel window coordinates.");}public void Dispose()=>SetThreadDpiAwarenessContext(previous);}
 private delegate bool EnumCallback(nint window,nint parameter);
 [DllImport("user32.dll")]private static extern bool EnumWindows(EnumCallback callback,nint parameter);
 [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint window);
 [DllImport("user32.dll")]private static extern bool IsWindow(nint window);
 [DllImport("user32.dll")]private static extern bool IsWindowEnabled(nint window);
 [DllImport("user32.dll")]private static extern bool IsIconic(nint window);
 [DllImport("user32.dll")]private static extern bool IsZoomed(nint window);
 [DllImport("user32.dll")]private static extern nint GetWindow(nint window,uint command);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint window,int index);
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
 [DllImport("user32.dll")]private static extern nint GetShellWindow();
 [DllImport("user32.dll")]private static extern nint GetDesktopWindow();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(nint window,StringBuilder name,int maximum);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetWindowPlacement(nint window,ref Placement placement);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool SetWindowPlacement(nint window,ref Placement placement);
 [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out Rect rect);
 [DllImport("user32.dll")]private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint window,uint flags);
 [DllImport("user32.dll")]private static extern nint MonitorFromRect(ref Rect rect,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInformation information);
 [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint context);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool SetProp(nint window,string name,nint value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint GetProp(nint window,string name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint RemoveProp(nint window,string name);
 [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(nint window,int attribute,out int value,int size);
}
