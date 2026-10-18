using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using SplitDeck.Core;
using Vortice.DXGI;

namespace SplitDeck.Windows;

public static class PerformanceProbe
{
 private sealed record CpuConsumer(string Name,int Id,double CpuPercent);
 private sealed record Sample(double CpuPercent,long AvailableMiB,double? GpuPercent,long? GpuMemoryMiB,CpuConsumer[] CpuConsumers);
 private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
 public static uint KeepDisplayAwake(bool active)=>SetThreadExecutionState(active?0x80000003u:0x80000000u);
 internal static LayoutProfile SelectProfile(LayoutProfile? saved,bool threeScreenTrial,int? diagnosticRefreshHz,LayoutProfile? diagnosticProfile)
 {
  var profile=diagnosticProfile??(threeScreenTrial?Presets.Triple(60):saved??Presets.Balanced(60));
  if(diagnosticRefreshHz is int hz){if(hz!=60)throw new ArgumentOutOfRangeException(nameof(diagnosticRefreshHz));profile=profile with{First=profile.First with{RefreshHz=hz},Second=profile.Second with{RefreshHz=hz},Third=profile.Third is { } third?third with{RefreshHz=hz}:null};}
  return profile;
 }
 public static async Task<int> RunAsync(string report,bool interactionWorkload=false,int? diagnosticRefreshHz=null,bool desktopMenuTrial=false,bool threeScreenTrial=false,LayoutProfile? diagnosticProfile=null)
 {
  var original=DisplayService.Snapshot();var display=new DisplayService();var monitors=display.Detect();
  if(monitors.Any(x=>x.IsVirtual))throw new InvalidOperationException("Restore the current split before starting the bounded benchmark.");
  var physical=monitors.FirstOrDefault(x=>x.IsPrimary&&!x.IsVirtual)??throw new InvalidOperationException("No physical primary monitor is available.");
  // Refuse topology changes if the normal desktop cannot currently be captured.
  var created=CaptureDevice.ForOutput(physical.DeviceName);
  using(var device=created.Device)using(var context=created.Context)using(var capture=new GpuDesktopCapture(physical.DeviceName,device,context,30)){
   var deadline=DateTime.UtcNow.AddSeconds(5);while(capture.View is null&&DateTime.UtcNow<deadline){capture.Update();await Task.Delay(20);}
   if(capture.View is null){var resources=new List<Sample>();for(int i=0;i<3;i++)resources.Add(await MeasureAsync());await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{Outcome="NotRun",Reason="The physical monitor did not produce a desktop frame. Wake/connect the monitor before testing.",Resources=resources,TopologyChanged=false},Json));return 2;}
  }
  if(!EditorElevation.IsElevated)throw new InvalidOperationException("The bounded session benchmark requires Windows administrator approval.");
  var profile=SelectProfile(new ProfileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SplitDeck")).Load(),threeScreenTrial,diagnosticRefreshHz,diagnosticProfile);
  LayoutEngine.Calculate(profile,physical.Width,physical.Height,physical.RefreshHz);
  if(threeScreenTrial){
   // Device creation is an explicit Setup operation, before the trial's baseline.
   // Do not treat a temporarily auto-attached driver output as the saved desktop.
   await using var preparation=new SessionController();
   try{await preparation.SetupDriverAsync(profile.Screens.Count);}
   finally{await preparation.WaitForHelperExitAsync(TimeSpan.FromSeconds(45));}
   await preparation.RefreshDriverReadinessAsync(profile.Screens.Count);
   if(!preparation.IsDriverInstalled||DriverInstallation.OwnedDeviceIds.Any(id=>DriverInstallation.InspectHealth(id).ProblemCode!=22))throw new InvalidOperationException("Three-screen Setup did not verify every managed driver instance disabled.");
   var afterSetup=DisplayService.Snapshot();if(afterSetup.Count!=original.Count||!original.All(afterSetup.Contains))throw new InvalidOperationException("Setup changed the physical desktop. Restore its original layout before starting the three-screen trial.");
   original=afterSetup;monitors=display.Detect();physical=monitors.Single(x=>x.DeviceName==physical.DeviceName&&!x.IsVirtual);
  }
  bool allHardwareNvidia=true;using(var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>()){
   for(uint index=0;factory.EnumAdapters1(index,out var adapter).Success;index++)using(adapter){var description=adapter.Description1;if((description.Flags&AdapterFlags.Software)==0&&description.VendorId!=0x10de)allHardwareNvidia=false;}
  }
  var baseline=new List<Sample>();InteractionFixtureResult? baselineWorkload=null,activeWorkload=null;
  if(interactionWorkload){
   await using var fixture=InteractionFixture.Start([physical with{Width=physical.Width/2},physical with{X=physical.X+physical.Width/2,Width=physical.Width/2}],TimeSpan.FromSeconds(20));
   await fixture.Ready.WaitAsync(TimeSpan.FromSeconds(5));var baselineDeadline=Stopwatch.StartNew();
   while(!fixture.Completion.IsCompleted&&baselineDeadline.Elapsed<TimeSpan.FromSeconds(25))baseline.Add(await MeasureAsync());
   if(!fixture.Completion.IsCompleted)throw new TimeoutException("The baseline interaction fixture stopped responding.");baselineWorkload=await fixture.Completion;
  }else for(int i=0;i<5;i++)baseline.Add(await MeasureAsync());
  double baselineCpu=baseline.Average(x=>x.CpuPercent);double? baselineGpu=MeanGpu(baseline);
  if(baselineCpu>75||baseline.Last().AvailableMiB<1024){await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{Outcome="NotRun",Reason="The baseline is already busy or memory is low. Close other heavy work before testing.",Baseline=baseline,TopologyChanged=false},Json));return 2;}
  await using var session=new SessionController();var active=new List<Sample>();var captureTimeline=new List<JsonElement>();string? error=null,abort=null;bool restored=false,helperExited=false,budgetExceeded=false;JsonElement? captureStats=null,initialStats=null;int violations=0;DateTime? activeStartedUtc=null,activeEndedUtc=null;
  try{
   using var startup=new CancellationTokenSource(TimeSpan.FromSeconds(25));await session.ApplyAsync(physical,profile,startup.Token);
   activeStartedUtc=DateTime.UtcNow;
   await Task.Delay(1000); // Exclude the display-mode transition from steady-state samples.
   initialStats=await ReadCaptureStatsAsync(session.HelperId);
   await using var fixture=interactionWorkload?InteractionFixture.Start(display.Detect().Where(x=>x.IsVirtual).OrderBy(x=>x.X).ToArray(),TimeSpan.FromSeconds(20)):null;
   if(fixture is not null)await fixture.Ready.WaitAsync(TimeSpan.FromSeconds(5));var workloadDeadline=Stopwatch.StartNew();
   for(int i=0;(fixture is null?(desktopMenuTrial||i<8):!fixture.Completion.IsCompleted)&&session.IsActive&&workloadDeadline.Elapsed<TimeSpan.FromSeconds(desktopMenuTrial?45:25);i++){
    var sample=await MeasureAsync();active.Add(sample);
    if(await ReadCaptureStatsAsync(session.HelperId) is JsonElement snapshot)captureTimeline.Add(snapshot);
    string? excess=PerformanceBudget.Excess(baselineCpu,sample.CpuPercent,sample.AvailableMiB,baselineGpu,sample.GpuPercent);
    budgetExceeded|=excess is not null;
    // The manual diagnostic observes brief overhead spikes, but can never pass the
    // budget when one occurs. Absolute resource guards still stop an unsafe trial.
    string? stopReason=desktopMenuTrial?(sample.AvailableMiB<512?"Memory below 512 MiB.":sample.CpuPercent>90?"CPU above 90%.":sample.GpuPercent>90?"GPU above 90%.":null):excess;
    violations=stopReason is null?0:violations+1;if(violations>=2){abort=stopReason;break;}
   }
   if(fixture is not null){if(!fixture.Completion.IsCompleted&&abort is null)abort="The interaction workload exceeded its independent deadline or the session stopped.";await fixture.DisposeAsync();activeWorkload=await fixture.Completion.WaitAsync(TimeSpan.FromSeconds(1));}
   captureStats=await ReadCaptureStatsAsync(session.HelperId);
  }catch(Exception e){error=e.Message;}
  finally{
   if(activeStartedUtc.HasValue)activeEndedUtc=DateTime.UtcNow;
   try{await session.RestoreAsync();}
   catch(Exception e){error=(error is null?"":error+" ")+"Restore: "+e.Message;}
   // Closing startup IPC is only a stop signal. Do not confuse it with completed recovery.
   try{
    await session.WaitForHelperExitAsync(TimeSpan.FromSeconds(45));helperExited=true;
    for(int attempt=0;attempt<20;attempt++){
     var current=DisplayService.Snapshot();bool journalComplete=true;
     string folder=Path.Combine(DriverInstallation.Root,"sessions");
     if(Directory.Exists(folder))foreach(string file in Directory.GetFiles(folder,"*.json").Where(x=>!x.EndsWith(".performance.json",StringComparison.OrdinalIgnoreCase))){
      var state=JsonSerializer.Deserialize<RecoveryJournal>(await File.ReadAllTextAsync(file));if(state is not null&&state.HelperId==session.HelperId&&!state.Complete)journalComplete=false;
     }
     restored=journalComplete&&current.Count==original.Count&&original.All(current.Contains);if(restored)break;await Task.Delay(500);
    }
   }catch(Exception e){error=(error is null?"":error+" ")+"Recovery unconfirmed: "+e.Message;}
  }
  bool gpuCoverage=allHardwareNvidia&&baseline.All(x=>x.GpuPercent.HasValue)&&active.All(x=>x.GpuPercent.HasValue);
  double? presentationFps=null,readbackMiBPerSecond=null,pollsPerSecond=null;
  bool adaptive=captureStats is JsonElement mode&&mode.TryGetProperty("AdaptivePresentation",out var adaptiveMode)&&adaptiveMode.GetBoolean();
  if(initialStats is JsonElement first&&captureStats is JsonElement last){double seconds=(last.GetProperty("UpdatedUtc").GetDateTime()-first.GetProperty("UpdatedUtc").GetDateTime()).TotalSeconds;if(seconds>0){presentationFps=(last.GetProperty("Frames").GetInt64()-first.GetProperty("Frames").GetInt64())/seconds;readbackMiBPerSecond=(last.GetProperty("CpuReadbackBytes").GetInt64()-first.GetProperty("CpuReadbackBytes").GetInt64())/seconds/1024/1024;}}
  if(initialStats is JsonElement pollStart&&captureStats is JsonElement pollEnd&&pollStart.TryGetProperty("PollsCompleted",out var startPolls)&&pollEnd.TryGetProperty("PollsCompleted",out var endPolls)){double seconds=(pollEnd.GetProperty("UpdatedUtc").GetDateTime()-pollStart.GetProperty("UpdatedUtc").GetDateTime()).TotalSeconds;if(seconds>0)pollsPerSecond=(endPolls.GetInt64()-startPolls.GetInt64())/seconds;}
  int targetHz=LayoutEngine.Calculate(profile,physical.Width,physical.Height,physical.RefreshHz).Max(x=>x.RefreshHz);bool presentationHealthy=PerformanceBudget.PresentationHealthy(presentationFps,targetHz);
  bool workloadCompleted=!interactionWorkload||(InteractionValidation.IsValid(baselineWorkload)&&InteractionValidation.IsValid(activeWorkload));
  bool healthy=error is null&&abort is null&&active.Count>=6&&restored&&helperExited&&workloadCompleted;bool pass=healthy&&gpuCoverage&&presentationHealthy&&(!desktopMenuTrial||!budgetExceeded);
  if(interactionWorkload||desktopMenuTrial)await File.WriteAllTextAsync(report+".workload.json",JsonSerializer.Serialize(new{Baseline=baselineWorkload,Active=activeWorkload,CaptureTimeline=captureTimeline,BudgetExceeded=budgetExceeded,Scope=interactionWorkload?"Programmatic actions in our own native fixture windows. This does not validate Explorer context menus, physical mouse input, or game responsiveness.":"Timed user-operated desktop menu diagnostic. Rendering telemetry alone does not verify which actions the user performed."},Json));
  await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{Outcome=pass?(interactionWorkload?"ControlledWorkloadBudgetPass":desktopMenuTrial?"ManualTrialWithinBudget":"DesktopBudgetPass"):healthy&&presentationHealthy&&!(desktopMenuTrial&&budgetExceeded)?"PartialMeasurements":"NeedsReview",HelperId=session.HelperId,ActiveStartedUtc=activeStartedUtc,ActiveEndedUtc=activeEndedUtc,Baseline=baseline,Active=active,BaselineCpuPercent=baselineCpu,ActiveCpuPercent=active.Count>0?(double?)active.Average(x=>x.CpuPercent):null,BaselineGpuPercent=baselineGpu,ActiveGpuPercent=MeanGpu(active),GpuCoverage=gpuCoverage?"All hardware adapters are NVIDIA and all samples include telemetry; utilization is the maximum across adapters, memory is summed.":"Incomplete: NVIDIA telemetry unavailable for some samples or non-NVIDIA hardware is present.",RequestedPresentationHz=targetHz,RequestedOutputRefreshHz=profile.OutputRefreshHz,AdaptivePresentation=adaptive,PollsPerSecond=pollsPerSecond,PresentationTargetMet=adaptive?(bool?)null:presentationHealthy,PresentationAssessment=adaptive?"Content-driven presentation: idle frames are skipped; assess animation separately. Inter-present intervals include intentional idle.":"Continuous presentation cadence",PresentationFramesPerSecond=presentationFps,CpuReadbackMiBPerSecond=readbackMiBPerSecond,CaptureStats=captureStats,Restored=restored,HelperExited=helperExited,Abort=abort,Error=error,BudgetExceeded=budgetExceeded,Scope=interactionWorkload?"Own-app native window workload only. No Explorer context-menu, physical mouse input, game, or physical-dual-monitor equivalence was tested.":desktopMenuTrial?"User-operated desktop trial. Rendering measurements do not verify user actions or game performance.":"Idle desktop trial only; presentation cadence is not game FPS. No game workload, FPS equivalence, or physical-dual-monitor comparison was measured."},Json));
  return pass?0:1;
 }
 private static async Task<JsonElement?> ReadCaptureStatsAsync(int? helperId)
 {
  string folder=Path.Combine(DriverInstallation.Root,"sessions");if(helperId is null||!Directory.Exists(folder))return null;
  foreach(string file in Directory.GetFiles(folder,"*.json.performance").OrderByDescending(File.GetLastWriteTimeUtc)){
   var state=JsonSerializer.Deserialize<RecoveryJournal>(await File.ReadAllTextAsync(file[..^".performance".Length]));
   if(state?.HelperId==helperId){using var parsed=JsonDocument.Parse(await File.ReadAllTextAsync(file));return parsed.RootElement.Clone();}
  }
  return null;
 }
 private static double? MeanGpu(IReadOnlyList<Sample> samples){var values=samples.Where(x=>x.GpuPercent.HasValue).Select(x=>x.GpuPercent!.Value).ToArray();return values.Length==0?null:values.Average();}
 private static async Task<Sample> MeasureAsync()
 {
  var processTimes=ReadProcessTimes();long processStarted=Stopwatch.GetTimestamp();
  if(!GetSystemTimes(out ulong idle0,out ulong kernel0,out ulong user0))throw new InvalidOperationException("Cannot read system CPU counters.");
  await Task.Delay(500);
  if(!GetSystemTimes(out ulong idle1,out ulong kernel1,out ulong user1))throw new InvalidOperationException("Cannot read system CPU counters.");
  var memory=new MemoryState{Length=(uint)Marshal.SizeOf<MemoryState>()};if(!GlobalMemoryStatusEx(ref memory))throw new InvalidOperationException("Cannot read available memory.");
  var after=ReadProcessTimes();double seconds=Stopwatch.GetElapsedTime(processStarted).TotalSeconds;
  var consumers=after.Where(x=>processTimes.TryGetValue(x.Key,out var previous)&&x.Value.Ticks>=previous.Ticks).Select(x=>new CpuConsumer(x.Value.Name,x.Key,(x.Value.Ticks-processTimes[x.Key].Ticks)/(double)TimeSpan.TicksPerSecond/seconds/Environment.ProcessorCount*100)).OrderByDescending(x=>x.CpuPercent).Take(6).ToArray();
  var gpu=await ReadGpuAsync();
  return new(PerformanceBudget.CpuPercent(idle0,kernel0+user0,idle1,kernel1+user1),(long)(memory.AvailablePhysical/1024/1024),gpu.Item1,gpu.Item2,consumers);
 }
 private static Dictionary<int,(string Name,long Ticks)> ReadProcessTimes()
 {
  var times=new Dictionary<int,(string,long)>();foreach(var process in Process.GetProcesses())using(process){try{if(process.Id!=0)times[process.Id]=(process.ProcessName,process.TotalProcessorTime.Ticks);}catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException){}}
  return times;
 }
 private static async Task<(double?,long?)> ReadGpuAsync()
 {
  string executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe");if(!File.Exists(executable))return(null,null);
  try{
   var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--query-gpu=utilization.gpu,memory.used");start.ArgumentList.Add("--format=csv,noheader,nounits");
   using var process=Process.Start(start);if(process is null)return(null,null);var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
   try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill();await process.WaitForExitAsync();return(null,null);}
   string text=await stdout;await stderr;if(process.ExitCode!=0)return(null,null);
   var rows=text.Split('\n',StringSplitOptions.RemoveEmptyEntries);double maximum=0;long memory=0;if(rows.Length==0)return(null,null);
   foreach(string row in rows){var parts=row.Split(',');if(parts.Length!=2||!double.TryParse(parts[0].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var utilization)||!long.TryParse(parts[1].Trim(),out var used))return(null,null);maximum=Math.Max(maximum,utilization);memory+=used;}
   return(maximum,memory);
  }catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException){return(null,null);}
 }
 [StructLayout(LayoutKind.Sequential)]private struct MemoryState{public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,AvailableExtended;}
 [DllImport("kernel32.dll")]private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
 [DllImport("kernel32.dll")]private static extern bool GlobalMemoryStatusEx(ref MemoryState state);
 [DllImport("kernel32.dll")]private static extern uint SetThreadExecutionState(uint flags);
}
