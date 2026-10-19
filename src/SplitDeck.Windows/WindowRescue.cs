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
 internal static IReadOnlyList<long> Family(IReadOnlyList<(long Handle,long Owner)> candidates,IReadOnlyCollection<long>? existingOwners=null)
 {
  var selected=candidates.Where(c=>c.Owner==0).Select(c=>c.Handle).Distinct().ToList();var seen=selected.ToHashSet();
  if(existingOwners is not null)seen.UnionWith(existingOwners);
  // EnumWindows returns z-order, so an owned dialog can precede its owner.
  // Resolve only chains rooted at an eligible captured app, never orphan tools.
  for(int pass=0;pass<16;pass++){int before=selected.Count;foreach(var candidate in candidates)if(candidate.Owner!=0&&seen.Contains(candidate.Owner)&&seen.Add(candidate.Handle))selected.Add(candidate.Handle);if(selected.Count==before)break;}
  return selected;
 }
 internal static bool ModalTool(bool owned,bool ownerDisabled,bool dialogStyle)=>owned&&ownerDisabled&&dialogStyle;
 internal static bool Eligible(bool visible,bool cloaked,bool owned,bool tool,bool noActivate,bool child,bool ownProcess,bool shell)=>visible&&!cloaked&&!owned&&!tool&&!noActivate&&!child&&!ownProcess&&!shell;
 internal static PixelRect ToScreen(PixelRect workspace,PixelRect monitor,PixelRect workArea)=>workspace with{X=checked(workspace.X+workArea.X-monitor.X),Y=checked(workspace.Y+workArea.Y-monitor.Y)};
 internal static PixelRect ToWorkspace(PixelRect screen,PixelRect monitor,PixelRect workArea)=>screen with{X=checked(screen.X-workArea.X+monitor.X),Y=checked(screen.Y-workArea.Y+monitor.Y)};
 internal static WindowRescueDestination Destination(PixelRect originalWindow,PixelRect source,IReadOnlyList<PixelRect> targets,IReadOnlyList<PixelRect> workAreas,double sizeScale=1)
 {
  if(source.Width<=0||source.Height<=0||originalWindow.Width<=0||originalWindow.Height<=0||targets.Count==0||targets.Count!=workAreas.Count||targets.Any(t=>t.Width<=0||t.Height<=0)||workAreas.Any(t=>t.Width<=0||t.Height<=0))throw new ArgumentException("Valid source and destination work areas are required.");
  if(!double.IsFinite(sizeScale)||sizeScale is <0.0625 or >16)throw new ArgumentOutOfRangeException(nameof(sizeScale),"Window size scaling must be finite and between 1/16 and 16.");
  double nx=Math.Clamp((originalWindow.X+originalWindow.Width/2.0-source.X)/source.Width,0,1),ny=Math.Clamp((originalWindow.Y+originalWindow.Height/2.0-source.Y)/source.Height,0,1);
  int left=targets.Min(t=>t.X),top=targets.Min(t=>t.Y);long right=targets.Max(t=>(long)t.X+t.Width),bottom=targets.Max(t=>(long)t.Y+t.Height);
  double x=left+nx*(right-left),y=top+ny*(bottom-top);
  double Distance(PixelRect t){double dx=Math.Max(t.X-x,Math.Max(0,x-((long)t.X+t.Width))),dy=Math.Max(t.Y-y,Math.Max(0,y-((long)t.Y+t.Height)));return dx*dx+dy*dy;}
  int index=Enumerable.Range(0,targets.Count).OrderBy(i=>Distance(targets[i])).First();var work=workAreas[index];
  // Scale only size after choosing a destination from the unchanged source
  // center. Recenter the scaled rectangle before clamping to the work area.
  int width=(int)Math.Clamp(Math.Round(originalWindow.Width*sizeScale),1,work.Width),height=(int)Math.Clamp(Math.Round(originalWindow.Height*sizeScale),1,work.Height);
  int wx=(int)Math.Clamp(Math.Round(x-width/2.0),work.X,(long)work.X+work.Width-width),wy=(int)Math.Clamp(Math.Round(y-height/2.0),work.Y,(long)work.Y+work.Height-height);
  return new(index,new(wx,wy,width,height));
 }
}
internal static class WindowRescue
{
 private const string IdentityProperty="SplitDeck.Rescue.Identity",MovedProperty="SplitDeck.Rescue.Moved",OffscreenIdentityProperty="SplitDeck.Rescue.Offscreen.Identity";
 private const uint AsyncPlacement=4,ShowWithoutActivation=8,AsyncPosition=0x4000,NoActivate=0x10,NoZOrder=4,NoOwnerZOrder=0x200;
 internal static List<WindowPlacementSnapshot> Capture(MonitorInfo selectedPhysical)
  =>CaptureFamily(Candidates(monitor=>string.Equals(monitor.DeviceName,selectedPhysical.DeviceName,StringComparison.OrdinalIgnoreCase)),[],IdentityProperty,false);
 // Enumerating and tagging does not move windows. The caller must persist the returned
 // combined list before removing virtual outputs, then use the ordinary RestoreAsync.
 internal static List<WindowPlacementSnapshot> CaptureAdditionalForRestore(IReadOnlyList<WindowPlacementSnapshot> existingSnapshots,MonitorInfo originalPhysical,IReadOnlyList<MonitorInfo> activeVirtuals,IReadOnlyDictionary<string,double>? renderDensities=null)
 {
  ArgumentNullException.ThrowIfNull(existingSnapshots);ArgumentNullException.ThrowIfNull(originalPhysical);ArgumentNullException.ThrowIfNull(activeVirtuals);
  var densities=ValidatedDensities(renderDensities);
  var combined=existingSnapshots.ToList();if(combined.Count>=256||activeVirtuals.Count==0)return combined;
  using var dpi=new DpiScope();var attached=AttachedMonitors();
  var virtualNames=activeVirtuals.Select(m=>m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
  var virtualBounds=attached.Where(m=>virtualNames.Contains(m.DeviceName)).Select(m=>AsRect(m.Bounds)).ToArray();
  if(virtualBounds.Length==0)return combined;
  var original=Bounds(originalPhysical);var currentPhysical=attached.FirstOrDefault(m=>string.Equals(m.DeviceName,originalPhysical.DeviceName,StringComparison.OrdinalIgnoreCase));
  var originalWork=original;
  if(!string.IsNullOrEmpty(currentPhysical.DeviceName)){
   var currentBounds=AsRect(currentPhysical.Bounds);var currentWork=AsRect(currentPhysical.WorkArea);
   var projected=currentWork with{X=checked(original.X+currentWork.X-currentBounds.X),Y=checked(original.Y+currentWork.Y-currentBounds.Y)};
   originalWork=Intersection(projected,original)??original;
  }
  // The taskbar may move to a virtual primary during the session. Prefer a
  // work area actually saved before Apply over the physical output's live one.
  var originalPlacement=existingSnapshots.FirstOrDefault(s=>s.SourceMonitor==original);
  if(originalPlacement is not null)originalWork=Intersection(originalPlacement.SourceWorkArea,original)??originalWork;
  int left=virtualBounds.Min(r=>r.X),top=virtualBounds.Min(r=>r.Y);
  var source=new PixelRect(left,top,checked((int)(virtualBounds.Max(r=>(long)r.X+r.Width)-left)),checked((int)(virtualBounds.Max(r=>(long)r.Y+r.Height)-top)));
  var known=combined.Select(s=>s.Handle).ToHashSet();
  var candidates=Candidates(monitor=>virtualNames.Contains(monitor.DeviceName),known).Select(saved=>{
   var sourceMonitor=attached.FirstOrDefault(m=>virtualNames.Contains(m.DeviceName)&&AsRect(m.Bounds)==saved.SourceMonitor);
   if(string.IsNullOrEmpty(sourceMonitor.DeviceName))throw new InvalidOperationException("The virtual source changed while its window restoration size was captured.");
   double density=Density(sourceMonitor.DeviceName,densities);
   var target=WindowRescuePolicy.Destination(saved.NormalScreenBounds,source,[original],[originalWork],1/density).Bounds;
   var placement=saved.Placement with{NormalPosition=WindowRescuePolicy.ToWorkspace(target,original,originalWork)};
   return saved with{Placement=placement,NormalScreenBounds=target,SourceMonitor=original,SourceWorkArea=originalWork};
  }).Take(256-combined.Count).ToArray();
  var owners=combined.Where(Matches).Select(s=>s.Handle).ToArray();
  combined.AddRange(CaptureFamily(candidates,owners,IdentityProperty,true));
  return combined;
 }
 internal static async Task<WindowRescueResult> RecoverOffscreenAsync(IReadOnlyList<MonitorInfo> physicalMonitors)
 {
  ArgumentNullException.ThrowIfNull(physicalMonitors);
  List<MonitorInformation> attached;List<WindowPlacementSnapshot> candidates;
  using(var dpi=new DpiScope()){
   attached=AttachedMonitors();
   candidates=Candidates(_=>true);
  }
  var physicalNames=physicalMonitors.Where(m=>!m.IsVirtual).Select(m=>m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
  var destinations=attached.Where(m=>physicalNames.Contains(m.DeviceName)).ToArray();
  if(destinations.Length==0)return new(0,0,0,0,[new(0,0,"No attached physical work area was available for offscreen recovery.")]);
  var byHandle=candidates.ToDictionary(c=>c.Handle);var offscreen=new List<WindowPlacementSnapshot>();
  using(var dpi=new DpiScope())foreach(long handle in WindowRescuePolicy.Family(candidates.Select(c=>(c.Handle,c.OwnerHandle)).ToArray())){
   var saved=byHandle[handle];
   if(IsOffscreen(saved,attached))offscreen.Add(saved);
  }
  // Keep session properties intact, including tokens left by an incomplete journal.
  var tagged=CaptureFamily(offscreen,offscreen.Select(s=>s.OwnerHandle).Where(h=>h!=0).ToArray(),OffscreenIdentityProperty,false);
  var taggedHandles=tagged.Select(s=>s.Handle).ToHashSet();
  var notTagged=offscreen.Where(s=>!taggedHandles.Contains(s.Handle)).Select(s=>new WindowRescueIssue(s.Handle,s.ProcessId,"The offscreen window could not be safely tagged and was left unchanged.")).ToArray();
  try{
   var result=await ApplyAsync(tagged,saved=>{
    using var dpi=new DpiScope();
    // A window can open or move while enumeration runs. Never replay a stale
    // offscreen decision over a window now intersecting any attached monitor.
    var current=AttachedMonitors();if(!IsOffscreen(saved,current))throw new InvalidOperationException("The window is now on an attached monitor and was left unchanged.");
    var target=current.Where(m=>destinations.Any(c=>string.Equals(c.DeviceName,m.DeviceName,StringComparison.OrdinalIgnoreCase)&&AsRect(c.Bounds)==AsRect(m.Bounds))).OrderBy(m=>Distance(saved.NormalScreenBounds,AsRect(m.WorkArea))).FirstOrDefault();
    if(string.IsNullOrEmpty(target.DeviceName))throw new InvalidOperationException("The physical destination changed before window recovery.");
    var bounds=AsRect(target.Bounds);var work=Intersection(AsRect(target.WorkArea),bounds)??bounds;
    return new Destination(Clamp(saved.NormalScreenBounds,work),bounds,work);
   },true,OffscreenIdentityProperty,false);
   return result with{Considered=offscreen.Count,Skipped=result.Skipped+notTagged.Length,Issues=result.Issues.Concat(notTagged).ToArray()};
  }finally{foreach(var saved in tagged)if(Matches(saved,OffscreenIdentityProperty))RemoveProp((nint)saved.Handle,OffscreenIdentityProperty);}
 }
 private static List<WindowPlacementSnapshot> Candidates(Func<MonitorInformation,bool> include,IReadOnlySet<long>? excluded=null)
 {
  using var dpi=new DpiScope();var candidates=new List<WindowPlacementSnapshot>();var started=Stopwatch.StartNew();var processStarts=new Dictionary<uint,long?>();
  EnumWindows((handle,_)=>{
   if(candidates.Count>=256||started.Elapsed>TimeSpan.FromSeconds(2))return false;
   try{
    if(excluded?.Contains(handle.ToInt64())==true)return true;
    GetWindowThreadProcessId(handle,out uint pid);var className=ClassName(handle);long style=GetWindowLongPtr(handle,-16).ToInt64(),extended=GetWindowLongPtr(handle,-20).ToInt64();
    int cloaked=0;DwmGetWindowAttribute(handle,14,out cloaked,4);
    bool shell=handle==GetShellWindow()||handle==GetDesktopWindow()||className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW"||className.StartsWith("SplitDeckHost_",StringComparison.Ordinal);
    nint owner=GetWindow(handle,4);
    bool modalTool=WindowRescuePolicy.ModalTool(owner!=0,owner!=0&&!IsWindowEnabled(owner),className=="#32770"||(extended&1)!=0);
    // Owned candidates are admitted only after their eligible owner family is known.
    if(!WindowRescuePolicy.Eligible(IsWindowVisible(handle),cloaked!=0,false,(extended&0x80)!=0&&!modalTool,(extended&0x08000000)!=0,(style&0x40000000)!=0,pid==Environment.ProcessId,shell))return true;
    if(!TryMonitor(handle,out var monitor)||!include(monitor))return true;
    var placement=Placement.New();if(!GetWindowPlacement(handle,ref placement))return true;
    var normal=AsRect(placement.NormalPosition);if(normal.Width<=0||normal.Height<=0)return true;
    if(!processStarts.TryGetValue(pid,out var processStarted)){processStarted=ProcessStarted(pid);processStarts.Add(pid,processStarted);}if(processStarted is null)return true;
    int token=RandomNumberGenerator.GetInt32(1,int.MaxValue);
    candidates.Add(new(handle.ToInt64(),pid,processStarted.Value,className,token,placement.Value(),WindowRescuePolicy.ToScreen(normal,AsRect(monitor.Bounds),AsRect(monitor.WorkArea)),AsRect(monitor.Bounds),AsRect(monitor.WorkArea),owner.ToInt64()));
   }catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or OverflowException){}
   return true;
  },0);
  return candidates;
 }
 private static List<WindowPlacementSnapshot> CaptureFamily(IReadOnlyList<WindowPlacementSnapshot> candidates,IReadOnlyCollection<long> existingOwners,string identityProperty,bool markForRestore)
 {
  var byHandle=candidates.ToDictionary(candidate=>candidate.Handle);var saved=new List<WindowPlacementSnapshot>();var captured=existingOwners.ToHashSet();
  foreach(long handle in WindowRescuePolicy.Family(candidates.Select(c=>(c.Handle,c.OwnerHandle)).ToArray(),existingOwners)){
   var candidate=byHandle[handle];if(candidate.OwnerHandle!=0&&!captured.Contains(candidate.OwnerHandle))continue;
   // Do not replace a token belonging to an existing or incomplete session.
   if(GetProp((nint)handle,identityProperty)!=0||!SameWindow(candidate)||!SetProp((nint)handle,identityProperty,(nint)candidate.Token))continue;
   if(markForRestore&&(GetProp((nint)handle,MovedProperty)!=0||!SetProp((nint)handle,MovedProperty,(nint)candidate.Token))){if(Matches(candidate,identityProperty))RemoveProp((nint)handle,identityProperty);continue;}
   saved.Add(candidate);captured.Add(handle);
  }
  return saved;
 }
 internal static Task<WindowRescueResult> MoveToVirtualsAsync(IReadOnlyList<WindowPlacementSnapshot> windows,MonitorInfo originalPhysical,IReadOnlyList<MonitorInfo> virtuals,IReadOnlyDictionary<string,double>? renderDensities=null)
 {
  var densities=ValidatedDensities(renderDensities);
  if(virtuals.Count==0)return Task.FromResult(new WindowRescueResult(windows.Count,0,0,windows.Count,[new(0,0,"No virtual destination was available; existing windows were left unchanged.")]));
  var bounds=virtuals.Select(Bounds).ToArray();var work=virtuals.Select(WorkArea).ToArray();var source=Bounds(originalPhysical);
  return ApplyAsync(windows,saved=>{
   var destination=WindowRescuePolicy.Destination(saved.NormalScreenBounds,source,bounds,work);
   double density=Density(virtuals[destination.Index].DeviceName,densities);
   if(density!=1)destination=WindowRescuePolicy.Destination(saved.NormalScreenBounds,source,bounds,work,density);
   return new Destination(destination.Bounds,bounds[destination.Index],work[destination.Index]);
  },false);
 }
 private static IReadOnlyDictionary<string,double> ValidatedDensities(IReadOnlyDictionary<string,double>? densities)
 {
  var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
  if(densities is not null)foreach(var entry in densities){
   if(string.IsNullOrWhiteSpace(entry.Key)||!double.IsFinite(entry.Value)||entry.Value is <0.0625 or >16)throw new ArgumentException("Render densities require a device name and a finite value between 1/16 and 16.",nameof(densities));
   if(!result.TryAdd(entry.Key,entry.Value))throw new ArgumentException("Render density device names must be unique, ignoring case.",nameof(densities));
  }
  return result;
 }
 private static double Density(string deviceName,IReadOnlyDictionary<string,double> densities)=>densities.TryGetValue(deviceName,out double density)?density:1;
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
 private static async Task<WindowRescueResult> ApplyAsync(IReadOnlyList<WindowPlacementSnapshot> windows,Func<WindowPlacementSnapshot,Destination> destination,bool restoring,string identityProperty=IdentityProperty,bool trackRestoration=true)
 {
  var issues=new List<WindowRescueIssue>();var pending=new List<Pending>();int skipped=0,queued=0,confirmed=0;
  foreach(var saved in windows.Take(256)){
   try{
    using var dpi=new DpiScope();var handle=(nint)saved.Handle;
    if(!Matches(saved,identityProperty)){skipped++;continue;}
    if(restoring&&trackRestoration&&GetProp(handle,MovedProperty)!=(nint)saved.Token){RemoveProp(handle,identityProperty);skipped++;continue;}
    var target=destination(saved);var placement=Placement.New();if(!GetWindowPlacement(handle,ref placement))throw new InvalidOperationException("Windows could not read the app's current placement.");
    bool minimized=IsIconic(handle),maximized=IsZoomed(handle);
    placement.Flags=(placement.Flags&2)|AsyncPlacement;placement.ShowCommand=ShowWithoutActivation;
    placement.NormalPosition=NativeRect(WindowRescuePolicy.ToWorkspace(target.Normal,target.Monitor,target.WorkArea));placement.MinPosition=new(-1,-1);placement.MaxPosition=new(-1,-1);
    if(!restoring&&trackRestoration&&!SetProp(handle,MovedProperty,(nint)saved.Token))throw new InvalidOperationException("Windows could not retain the app's restoration identity.");
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
    if(!Matches(item.Saved,identityProperty)){pending.RemoveAt(i);skipped++;continue;}
    var placement=Placement.New();bool states=IsIconic(handle)==item.Minimized&&IsZoomed(handle)==item.Maximized;
    bool bounds=GetWindowPlacement(handle,ref placement)&&Near(WindowRescuePolicy.ToScreen(AsRect(placement.NormalPosition),item.Target.Monitor,item.Target.WorkArea),item.Target.Normal);
    if(!item.Minimized)bounds=bounds&&GetWindowRect(handle,out var actual)&&Near(AsRect(actual),item.Maximized?item.Target.WorkArea:item.Target.Normal);
    if(states&&bounds){confirmed++;pending.RemoveAt(i);if(restoring){RemoveProp(handle,identityProperty);if(trackRestoration)RemoveProp(handle,MovedProperty);}}
   }
   if(pending.Count>0)await Task.Delay(40);
  }
  foreach(var item in pending)issues.Add(new(item.Saved.Handle,item.Saved.ProcessId,"The app did not confirm the requested bounds and unchanged minimize/maximize state within two seconds."));
  return new(windows.Count,queued,confirmed,skipped,issues);
 }
 private static bool Near(PixelRect a,PixelRect b)=>Math.Abs((long)a.X-b.X)<=12&&Math.Abs((long)a.Y-b.Y)<=12&&Math.Abs((long)a.Width-b.Width)<=24&&Math.Abs((long)a.Height-b.Height)<=24;
 private static bool Matches(WindowPlacementSnapshot saved)=>Matches(saved,IdentityProperty);
 private static bool Matches(WindowPlacementSnapshot saved,string identityProperty)=>saved.Token>0&&GetProp((nint)saved.Handle,identityProperty)==(nint)saved.Token&&SameWindow(saved);
 private static bool SameWindow(WindowPlacementSnapshot saved)=>IsWindow((nint)saved.Handle)&&GetWindowThreadProcessId((nint)saved.Handle,out uint pid)!=0&&pid==saved.ProcessId&&ClassName((nint)saved.Handle)==saved.ClassName&&ProcessStarted(pid)==saved.ProcessStartedUtcTicks;
 private static bool IsOffscreen(WindowPlacementSnapshot saved,IReadOnlyList<MonitorInformation> attached)
 {
  if(!SameWindow(saved)||attached.Count==0)return false;
  PixelRect bounds;
  if(IsIconic((nint)saved.Handle)){
   var placement=Placement.New();if(!GetWindowPlacement((nint)saved.Handle,ref placement)||!TryMonitor((nint)saved.Handle,out var monitor))return false;
   bounds=WindowRescuePolicy.ToScreen(AsRect(placement.NormalPosition),AsRect(monitor.Bounds),AsRect(monitor.WorkArea));
  }else{if(!GetWindowRect((nint)saved.Handle,out var actual))return false;bounds=AsRect(actual);}
  return bounds.Width>0&&bounds.Height>0&&!attached.Any(m=>Intersection(bounds,AsRect(m.Bounds)) is not null);
 }
 private static PixelRect? Intersection(PixelRect a,PixelRect b){long x=Math.Max(a.X,b.X),y=Math.Max(a.Y,b.Y),right=Math.Min((long)a.X+a.Width,(long)b.X+b.Width),bottom=Math.Min((long)a.Y+a.Height,(long)b.Y+b.Height);return right>x&&bottom>y?new((int)x,(int)y,checked((int)(right-x)),checked((int)(bottom-y))):null;}
 private static PixelRect Clamp(PixelRect rect,PixelRect work){int width=Math.Min(rect.Width,work.Width),height=Math.Min(rect.Height,work.Height);return new((int)Math.Clamp((long)rect.X,work.X,(long)work.X+work.Width-width),(int)Math.Clamp((long)rect.Y,work.Y,(long)work.Y+work.Height-height),width,height);}
 private static double Distance(PixelRect window,PixelRect work){double x=window.X+window.Width/2.0,y=window.Y+window.Height/2.0,dx=Math.Max(work.X-x,Math.Max(0,x-((long)work.X+work.Width))),dy=Math.Max(work.Y-y,Math.Max(0,y-((long)work.Y+work.Height)));return dx*dx+dy*dy;}
 private static List<MonitorInformation> AttachedMonitors()
 {
  var monitors=new List<MonitorInformation>();
  if(!EnumDisplayMonitors(0,0,(nint monitor,nint _,ref Rect unused,nint parameter)=>{var info=MonitorInformation.New();if(GetMonitorInfo(monitor,ref info)&&info.Bounds.Right>info.Bounds.Left&&info.Bounds.Bottom>info.Bounds.Top)monitors.Add(info);return true;},0))throw new InvalidOperationException("Windows could not enumerate attached monitor bounds.");
  return monitors;
 }
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
 private delegate bool MonitorCallback(nint monitor,nint dc,ref Rect bounds,nint parameter);
 [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint parameter);
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
