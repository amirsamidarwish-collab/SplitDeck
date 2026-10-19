using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SplitDeck.Core;

namespace SplitDeck.Windows;

internal sealed record RecoveryJournal(int HelperId,long HelperStarted,List<DisplaySnapshot> Original,List<DisplaySnapshot> Expected,string? DriverSettings,int CursorX,int CursorY,ClipSnapshot Clip,bool Complete,bool RestartRequired=false,List<WindowPlacementSnapshot>? Windows=null,List<VirtualDpiChange>? DpiChanges=null,string? SelectedMonitor=null,bool RetainDiagnostics=false,bool LateWindowsCaptured=true);
internal sealed record ClipSnapshot(int Left,int Top,int Right,int Bottom);
public static class HelperRuntime
{
 public static bool IsHelperCommand(string[] args)=>args.Length>0&&(args[0]=="--helper"||args[0]=="--recovery-watch");
 public static async Task<int> RunAsync(string[] args)
 {
  if(args.Length!=2)return 2;
  if(args[0]=="--recovery-watch")return await WatchAsync(args[1]);
  if(args[0]!="--helper")return 2;
  var identity=WindowsIdentity.GetCurrent();if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 5;
  string requests=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SplitDeck","requests"))+Path.DirectorySeparatorChar;
  string path=Path.GetFullPath(args[1]);if(!path.StartsWith(requests,StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length>32768)return 3;
  var request=JsonSerializer.Deserialize<HelperRequest>(await File.ReadAllTextAsync(path))??throw new IOException("Invalid request.");
  if(!request.Pipe.StartsWith("SplitDeck-",StringComparison.Ordinal)||request.Pipe.Length!=42||request.Token.Length!=64||request.Mode is not("install" or "apply")||request.ScreenCount is not(2 or 3))return 3;
  using var parent=Process.GetProcessById(request.ParentId);if(parent.StartTime.ToUniversalTime().Ticks!=request.ParentStarted)return 3;
  using var pipe=new NamedPipeClientStream(".",request.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous);using var connect=new CancellationTokenSource(TimeSpan.FromSeconds(30));await pipe.ConnectAsync(connect.Token);
  using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};await writer.WriteLineAsync(request.Token);
  using var writes=new SemaphoreSlim(1,1);async Task Send(string type,string text){await writes.WaitAsync();try{await writer.WriteLineAsync(JsonSerializer.Serialize(new HelperMessage(type,text)));}finally{writes.Release();}}
  try{
   using var lease=await MachineLease.AcquireAsync(TimeSpan.FromSeconds(5));await RecoverPendingAsync();
   if(request.Mode=="install"){await Send("STATUS",$"Verifying and preparing {request.ScreenCount} signed driver instances…");await DriverInstallation.InstallAsync(request.ScreenCount);await Send("COMPLETE","Driver setup complete. No split is active. You can now Apply a layout.");return 0;}
   await ActivateAsync(request,parent,reader,Send);return 0;
  }catch(Exception e){try{await Send("ERROR",e.Message);}catch(IOException){}return 1;}
 }
 private static async Task ActivateAsync(HelperRequest request,Process parent,StreamReader reader,Func<string,string,Task> send)
 {
  var display=new DisplayService();var physical=display.Detect().SingleOrDefault(m=>m.DeviceName==request.Monitor&&!m.IsVirtual)??throw new InvalidOperationException("The selected physical monitor is no longer connected.");
  var originalPhysical=physical;
  int physicalDpi=VirtualDpiScaling.PhysicalDpi(physical);
  if(request.Profile is null)throw new ArgumentException("A layout is required.");var layouts=LayoutEngine.Calculate(request.Profile,physical.Width,physical.Height,physical.RefreshHz);
  int? outputHz=OutputRefreshPolicy.Select(request.Profile.OutputRefreshHz,layouts,physical.RefreshHz);if(outputHz is int selectedHz)DisplayService.RequireSupportedOutputRefresh(physical,selectedHz);
  DriverInstallation.RequireOwned(layouts.Count);
  var originals=DisplayService.Snapshot();if(display.Detect().Any(m=>m.IsVirtual))throw new InvalidOperationException("Virtual displays are already active. Restore or stop that session first.");
  if(!Native.GetCursorPos(out var cursor)||!Native.GetClipCursor(out var clip))throw new InvalidOperationException("Windows could not read the original cursor state. Keep Windows unlocked and try again.");using var self=Process.GetCurrentProcess();
  var journal=new RecoveryJournal(self.Id,self.StartTime.ToUniversalTime().Ticks,originals,originals,File.Exists(DriverInstallation.SettingsPath)?await File.ReadAllTextAsync(DriverInstallation.SettingsPath):null,cursor.X,cursor.Y,new(clip.Left,clip.Top,clip.Right,clip.Bottom),false,Windows:WindowRescue.Capture(physical),SelectedMonitor:physical.DeviceName,RetainDiagnostics:request.RetainDiagnostics);
  string journalFolder=Path.Combine(DriverInstallation.Root,"sessions");Directory.CreateDirectory(journalFolder);string journalPath=Path.Combine(journalFolder,Guid.NewGuid().ToString("N")+".json");DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));
  using var guard=SessionController.StartChild(["--recovery-watch",journalPath],false);
  await WatchdogReadiness.WaitAsync(()=>File.Exists(journalPath+".ready"),()=>guard.HasExited,TimeSpan.FromSeconds(10));
  using var cancellation=new CancellationTokenSource();var recovery=new RecoveryStack();PresentationHost? host=null;
  recovery.Push(async()=>{if(TopologyGuard.CanRestore(originals,originals,DisplayService.Snapshot()))await RestoreWindowsAsync(journalPath,journal.Windows??[]);});
  recovery.Push(()=>{DisplayService.RestoreOwned(originals,journal.Expected);return Task.CompletedTask;});
  recovery.Push(()=>{if(journal.DriverSettings is not null)DriverInstallation.AtomicWrite(DriverInstallation.SettingsPath,journal.DriverSettings);else File.Delete(DriverInstallation.SettingsPath);return Task.CompletedTask;});
  recovery.Push(()=>DriverInstallation.DisableAsync());
  recovery.Push(()=>VirtualDpiScaling.ApplyAsync(journal.DpiChanges??[],restoring:true,onRestored:change=>{journal=journal with{DpiChanges=journal.DpiChanges!.Select(item=>item.DeviceKey==change.DeviceKey?item with{Restored=true}:item).ToList()};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));}));
  MonitorInfo[] activeVirtuals=[];
  Exception? activationError=null;
  try{
   await send("STATUS",$"Configuring {layouts.Count} virtual display modes…");DriverInstallation.Configure(layouts);await DriverInstallation.EnableAsync(layouts.Count);journal=journal with{Expected=DisplayService.Snapshot(),LateWindowsCaptured=false};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));
   DriverInstallation.AtomicWrite(journalPath+".display-adapters",JsonSerializer.Serialize(DisplayService.AdapterInventory()));
   var virtuals=await WaitForVirtualsAsync(display,layouts,cancellation.Token);journal=journal with{Expected=DisplayService.Snapshot()};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));
   // Windows requires the explicitly selected primary at (0,0); left columns can be negative.
   var plannedModes=SessionDisplayPlan.Build(virtuals,layouts,request.Profile.PrimaryIndex);
   var physicalModes=SessionDisplayPlan.PhysicalModes(originals,physical.DeviceName,plannedModes,outputHz);
   foreach(var mode in plannedModes.OrderByDescending(m=>m.Primary))DisplayService.SetMode(mode.DeviceName,mode.X,mode.Y,mode.Width,mode.Height,mode.Hz,mode.Primary);
   foreach(var s in physicalModes)DisplayService.SetMode(s.DeviceName,s.X,s.Y,s.Width,s.Height,s.Hz);
   // Persist intent before commit. A crash during commit conservatively refuses stale replay.
   journal=journal with{Expected=physicalModes.ToList()};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));
   DisplayService.Commit();journal=journal with{Expected=DisplayService.Snapshot()};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));await Task.Delay(700);
   var all=display.Detect();
   journal=journal with{Expected=DisplayService.Snapshot()};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));
   var mismatches=new List<string>();
   foreach(var mode in plannedModes){var actual=all.SingleOrDefault(m=>m.DeviceName==mode.DeviceName);if(actual is null||actual.X!=mode.X||actual.Y!=mode.Y||actual.Width!=mode.Width||actual.Height!=mode.Height||Math.Abs(actual.RefreshHz-mode.Hz)>1||actual.IsPrimary!=mode.Primary)mismatches.Add($"{mode.DeviceName}: requested ({mode.X},{mode.Y}) {mode.Width}x{mode.Height}@{mode.Hz} primary={mode.Primary}; actual "+(actual is null?"missing":$"({actual.X},{actual.Y}) {actual.Width}x{actual.Height}@{actual.RefreshHz} primary={actual.IsPrimary}"));}
   foreach(var mode in physicalModes){var actual=all.SingleOrDefault(m=>m.DeviceName==mode.DeviceName);if(actual is null||actual.X!=mode.X||actual.Y!=mode.Y||actual.Width!=mode.Width||actual.Height!=mode.Height||Math.Abs(actual.RefreshHz-mode.Hz)>0.5||actual.IsPrimary)mismatches.Add($"{mode.DeviceName}: requested physical ({mode.X},{mode.Y}) {mode.Width}x{mode.Height}@{mode.Hz}; actual "+(actual is null?"missing":$"({actual.X},{actual.Y}) {actual.Width}x{actual.Height}@{actual.RefreshHz} primary={actual.IsPrimary}"));}
   WriteTopologyResult(journalPath+".topology",plannedModes,physicalModes,all,mismatches);
   if(mismatches.Count>0)throw new InvalidOperationException("Windows changed the requested display topology. "+string.Join("; ",mismatches));
   physical=all.Single(x=>x.DeviceName==physical.DeviceName);virtuals=virtuals.Select(v=>all.Single(x=>x.DeviceName==v.DeviceName)).ToArray();
   activeVirtuals=virtuals;
   // Persist the original scale before writing even the first source.
   journal=journal with{DpiChanges=VirtualDpiScaling.Plan(virtuals,layouts,physicalDpi)};
   DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));
   await VirtualDpiScaling.ApplyAsync(journal.DpiChanges??[]);
   DisplayService.RestoreBootDefaults(originals,journal.Expected);
   host=new PresentationHost(physical,virtuals.Select((v,i)=>new PresentationSurface(v,layouts[i])).ToArray(),()=>cancellation.Cancel());
   recovery.Push(()=>{host.Dispose();return Task.CompletedTask;});
   await host.Ready.WaitAsync(TimeSpan.FromSeconds(20));
   var windowsMoved=await WindowRescue.MoveToVirtualsAsync(journal.Windows??[],originalPhysical,virtuals,RenderDensities(journal));WriteWindowResult(journalPath+".window-move",windowsMoved);
   recovery.Push(()=>{Native.ReleaseCursor(0);return Task.CompletedTask;});int minX=virtuals.Min(v=>v.X),minY=virtuals.Min(v=>v.Y),right=virtuals.Max(v=>v.X+v.Width),bottom=virtuals.Max(v=>v.Y+v.Height);var virtualBounds=new Native.Rect(minX,minY,right-minX,bottom-minY);if(!Native.ClipCursor(ref virtualBounds))throw new InvalidOperationException("Windows could not confine the pointer to the virtual screens.");var primary=virtuals[request.Profile.PrimaryIndex];Native.SetCursorPos(primary.X+primary.Width/2,primary.Y+primary.Height/2);
   var cursorBoundary=new CursorBoundaryGuard(virtuals.Select(v=>new PixelRect(v.X,v.Y,v.Width,v.Height)).ToArray(),
    ()=>{bool ok=Native.GetClipCursor(out var r);return(ok,new PixelRect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top));},
    ()=>{bool ok=Native.GetCursorPos(out var p);return(ok,p.X,p.Y);},
    r=>{var native=new Native.Rect(r.X,r.Y,r.Width,r.Height);return Native.ClipCursor(ref native);},Native.SetCursorPos);
   await send("ACTIVE",host.CaptureSummary+". "+(windowsMoved.Issues.Count>0?"Some app windows could not be moved automatically. ":"")+"Restore desktop or Ctrl+Alt+Shift+F12 ends the split.");
   Task<string?> incoming=reader.ReadLineAsync();long polls=Interlocked.Read(ref host.PollsCompleted);var loopProgress=DateTime.UtcNow;var nextTelemetry=DateTime.MinValue;
   while(!cancellation.IsCancellationRequested){
    long completed=Interlocked.Read(ref host.PollsCompleted);if(completed!=polls){polls=completed;loopProgress=DateTime.UtcNow;}else if(DateTime.UtcNow-loopProgress>TimeSpan.FromSeconds(5))throw new TimeoutException("Presentation polling stopped responding; restoring desktop.");
    if(DateTime.UtcNow>=nextTelemetry){File.WriteAllText(journalPath+".heartbeat","");WritePerformance(journalPath,host);nextTelemetry=DateTime.UtcNow.AddSeconds(1);}
    if(parent.HasExited)break;if(host.Failure is not null)throw new InvalidOperationException("Presentation failed. "+host.Failure.Message);
    if(incoming.IsCompleted){string? command=await incoming;if(command is null||command=="STOP")break;incoming=reader.ReadLineAsync();}
    var boundaryResult=cursorBoundary.Tick();if(boundaryResult.ClipChanged)Interlocked.Increment(ref host.CursorBoundaryRepairs);if(boundaryResult.PositionChanged)Interlocked.Increment(ref host.CursorPositionRepairs);
    await Task.Delay(100);
   }
  }catch(Exception e){activationError=e;}
  finally{
   if(host is not null)WritePerformance(journalPath,host);
   // Capture late-created app windows while their virtual coordinates still
   // identify the source. Persist them before any display is disabled.
   try{if(!journal.LateWindowsCaptured&&activeVirtuals.Length==0)throw new InvalidOperationException("Late app-window capture could not be confirmed before display teardown.");journal=journal with{Windows=WindowRescue.CaptureAdditionalForRestore(journal.Windows??[],originalPhysical,activeVirtuals,RenderDensities(journal)),LateWindowsCaptured=true};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));}
   catch(Exception e){journal=journal with{LateWindowsCaptured=false};activationError=activationError is null?e:new AggregateException(activationError,e);}
   Native.ReleaseCursor(0);try{await recovery.RevertAsync();var previous=new Native.Rect(clip.Left,clip.Top,clip.Right-clip.Left,clip.Bottom-clip.Top);Native.ClipCursor(ref previous);Native.SetCursorPos(cursor.X,cursor.Y);if(!journal.LateWindowsCaptured)throw new InvalidOperationException("Late app-window recovery remains unverified; its journal was retained.");journal=journal with{Complete=true,RestartRequired=false};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));await send("RESTORED","Original desktop restored.");}catch(Exception e){journal=journal with{RestartRequired=journal.RestartRequired||NeedsRestart(e)};DriverInstallation.AtomicWrite(journalPath,JsonSerializer.Serialize(journal));throw new AggregateException(journal.RestartRequired?"Windows requires a restart to finish disabling the virtual driver. The recovery journal remains incomplete.":"Restoration needs attention. The recovery journal was retained.",activationError is null?[e]:[activationError,e]);}
  }
  if(activationError is not null)throw activationError;
 }
 internal static string PerformancePath(string journalPath)=>journalPath+".performance";
 private static void WriteTopologyResult(string path,IReadOnlyList<SessionDisplayMode> virtuals,IReadOnlyList<DisplaySnapshot> physical,IReadOnlyList<MonitorInfo> actual,IReadOnlyList<string> mismatches){try{DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(new{RequestedVirtual=virtuals,RequestedPhysical=physical,Actual=actual,Mismatches=mismatches}));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
 private static void WriteWindowResult(string path,WindowRescueResult result){try{DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(result));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
 private static async Task RestoreWindowsAsync(string path,IReadOnlyList<WindowPlacementSnapshot> windows)
 {
  var result=await WindowRescue.RestoreAsync(windows);WriteWindowResult(path+".window-restore",result);
  if(result.Issues.Count>0)throw new InvalidOperationException("The physical desktop was restored, but some app window positions are unconfirmed. The recovery journal was retained.");
 }
 private static IReadOnlyDictionary<string,double> RenderDensities(RecoveryJournal journal)
 {
  var displays=DisplayService.Snapshot();var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
  foreach(var change in journal.DpiChanges??[]){var display=displays.FirstOrDefault(d=>string.Equals(d.DeviceKey,change.DeviceKey,StringComparison.OrdinalIgnoreCase));if(display is not null)result[display.DeviceName]=change.RenderDensity;}
  return result;
 }
 private static void WritePerformance(string journalPath,PresentationHost host)
 {
  try{var timing=host.TimingSnapshot;DriverInstallation.AtomicWrite(PerformancePath(journalPath),JsonSerializer.Serialize(new{UpdatedUtc=DateTime.UtcNow,Frames=Interlocked.Read(ref host.FramesPresented),PollsCompleted=Interlocked.Read(ref host.PollsCompleted),IdleSkipped=Interlocked.Read(ref host.IdleSkipped),host.AdaptivePresentation,host.ImmediatePresentation,host.OutputRefreshHz,host.GpuCaptureStreams,host.CpuFallbackCaptureStreams,host.CpuReadbackBytes,host.GpuCapturedFrames,host.GpuProfileSnapshot,CaptureMilliseconds=Interlocked.Read(ref host.CaptureTicks)*1000.0/Stopwatch.Frequency,DrawMilliseconds=Interlocked.Read(ref host.DrawTicks)*1000.0/Stopwatch.Frequency,PresentMilliseconds=Interlocked.Read(ref host.PresentTicks)*1000.0/Stopwatch.Frequency,timing.GpuUpdateMaxMilliseconds,timing.DrawMaxMilliseconds,timing.CursorMaxMilliseconds,timing.PresentMaxMilliseconds,timing.PresentIntervalMaxMilliseconds,timing.PresentIntervalsOver100Ms,host.CaptureSummary,host.CaptureDetails,host.CursorDiagnostic,host.CursorBoundaryRepairs,host.CursorPositionRepairs}));}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
 }
 private static async Task<MonitorInfo[]> WaitForVirtualsAsync(DisplayService display,IReadOnlyList<VirtualScreenLayout> layouts,CancellationToken ct)
 {
  var ownedKeys=DriverInstallation.OwnedDisplayKeys(layouts.Count);
  for(int i=0;i<60;i++){
   ct.ThrowIfCancellationRequested();
   var names=DisplayService.AdapterInventory().Where(a=>string.Equals(a.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase)&&ownedKeys.Contains(a.DeviceKey,StringComparer.OrdinalIgnoreCase)).Select(a=>a.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
   var result=display.Detect().Where(x=>x.IsVirtual&&names.Contains(x.DeviceName)).OrderBy(x=>x.DeviceName,StringComparer.Ordinal).ToArray();if(result.Length==layouts.Count)return result;
   DisplayService.AttachInactiveOwned(ownedKeys,layouts);await Task.Delay(250,ct);
  }
  throw new TimeoutException($"Windows did not attach all {layouts.Count} managed virtual displays with their requested modes. The saved display-adapters diagnostic records their enumeration state.");
 }
 private static async Task<int> WatchAsync(string path)
 {
  string allowed=Path.GetFullPath(Path.Combine(DriverInstallation.Root,"sessions"))+Path.DirectorySeparatorChar;if(!Path.GetFullPath(path).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))return 3;
  var journal=JsonSerializer.Deserialize<RecoveryJournal>(await File.ReadAllTextAsync(path));if(journal is null||journal.Complete)return 0;
  DriverInstallation.AtomicWrite(path+".ready","armed");
  try{using var process=Process.GetProcessById(journal.HelperId);if(process.StartTime.ToUniversalTime().Ticks==journal.HelperStarted){
   while(!process.HasExited){var heartbeat=path+".heartbeat";var last=File.Exists(heartbeat)?File.GetLastWriteTimeUtc(heartbeat):File.GetLastWriteTimeUtc(path+".ready");if(DateTime.UtcNow-last>TimeSpan.FromSeconds(120)){process.Kill();await process.WaitForExitAsync();break;}await Task.Delay(500);}
  }}catch(ArgumentException){}
  using var lease=await MachineLease.AcquireAsync(TimeSpan.FromSeconds(30));int result=await RecoverJournalAsync(path);if(result==0)SessionArtifacts.PruneCompleted(path);return result;
 }
 private static async Task RecoverPendingAsync()
 {
  string folder=Path.Combine(DriverInstallation.Root,"sessions");if(!Directory.Exists(folder))return;
  foreach(string path in Directory.GetFiles(folder,"*.json")){if(await RecoverJournalAsync(path)!=0)throw new InvalidOperationException("An earlier recovery requires attention. Review "+path+".error.txt before starting another session.");SessionArtifacts.PruneCompleted(path);}
 }
 private static async Task<int> RecoverJournalAsync(string path)
 {
  if(!File.Exists(path))return 0;
  var journal=JsonSerializer.Deserialize<RecoveryJournal>(await File.ReadAllTextAsync(path));if(journal is null||journal.Complete)return 0;
  try{
   var saved=journal.Original.FirstOrDefault(m=>m.DeviceName==journal.SelectedMonitor)??journal.Original.FirstOrDefault(m=>m.Primary);
   if(saved is not null){
    var ownedKeys=DriverInstallation.OwnedDisplayKeys();
    var virtuals=DisplayService.Snapshot().Where(m=>string.Equals(m.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase)&&ownedKeys.Contains(m.DeviceKey,StringComparer.OrdinalIgnoreCase)).Select(m=>new MonitorInfo(m.DeviceName,"SplitDeck",m.X,m.Y,m.Width,m.Height,m.Hz,null,m.Primary,true)).ToArray();
    var physical=new MonitorInfo(saved.DeviceName,"Physical",saved.X,saved.Y,saved.Width,saved.Height,saved.Hz,null,saved.Primary,false);
    if(!journal.LateWindowsCaptured&&virtuals.Length==0)throw new InvalidOperationException("Late app-window recovery is pending and the virtual source is unavailable.");
    journal=journal with{Windows=WindowRescue.CaptureAdditionalForRestore(journal.Windows??[],physical,virtuals,RenderDensities(journal)),LateWindowsCaptured=true};DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(journal));
   }
  }catch(Exception error)when(error is IOException or InvalidOperationException or UnauthorizedAccessException){journal=journal with{LateWindowsCaptured=false};/* Continue physical cleanup but retain the unverified window-recovery state. */}
  bool OriginalRestored()=>TopologyGuard.CanRestore(journal.Original,journal.Original,DisplayService.Snapshot());
  var recovery=new RecoveryStack();
  recovery.Push(async()=>{if(OriginalRestored())await RestoreWindowsAsync(path,journal.Windows??[]);});
  recovery.Push(()=>{if(OriginalRestored())Native.SetCursorPos(journal.CursorX,journal.CursorY);return Task.CompletedTask;});
  recovery.Push(()=>{if(!OriginalRestored())return Task.CompletedTask;var c=journal.Clip;var previous=new Native.Rect(c.Left,c.Top,c.Right-c.Left,c.Bottom-c.Top);Native.ClipCursor(ref previous);return Task.CompletedTask;});
  recovery.Push(()=>{DisplayService.RestoreOwned(journal.Original,journal.Expected);return Task.CompletedTask;});
  recovery.Push(()=>{if(journal.DriverSettings is not null)DriverInstallation.AtomicWrite(DriverInstallation.SettingsPath,journal.DriverSettings);else File.Delete(DriverInstallation.SettingsPath);return Task.CompletedTask;});
  recovery.Push(()=>DriverInstallation.DisableAsync());
  recovery.Push(()=>VirtualDpiScaling.ApplyAsync(journal.DpiChanges??[],restoring:true,onRestored:change=>{journal=journal with{DpiChanges=journal.DpiChanges!.Select(item=>item.DeviceKey==change.DeviceKey?item with{Restored=true}:item).ToList()};DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(journal));}));
  recovery.Push(()=>{Native.ReleaseCursor(0);return Task.CompletedTask;});
  try{await recovery.RevertAsync();if(!journal.LateWindowsCaptured)throw new InvalidOperationException("Late app-window recovery is still unverified; the recovery journal was retained.");DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(journal with{Complete=true,RestartRequired=false}));return 0;}catch(Exception e){journal=journal with{RestartRequired=journal.RestartRequired||NeedsRestart(e)};DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(journal));await File.WriteAllTextAsync(path+".error.txt",e.ToString());return 1;}
 }
 private static bool NeedsRestart(Exception error)=>error is DriverRestartRequiredException||error is AggregateException aggregate&&aggregate.Flatten().InnerExceptions.Any(NeedsRestart);
}
