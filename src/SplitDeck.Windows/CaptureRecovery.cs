namespace SplitDeck.Windows;

internal sealed class DuplicationRecovery
{
 private readonly long timeout,retryDelay;private long? started;private long nextAttempt;
 internal DuplicationRecovery(long ticksPerSecond){if(ticksPerSecond<=0)throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));timeout=checked(ticksPerSecond*3);retryDelay=Math.Max(1,ticksPerSecond/10);}
 internal static bool IsRecoverable(int result)=>result==unchecked((int)0x887A0026);
 internal void Begin(long now){if(started is null){started=now;nextAttempt=now;}}
 internal bool ShouldAttempt(long now){ThrowIfExpired(now);if(started is null||now<nextAttempt)return false;nextAttempt=now+retryDelay;return true;}
 internal void ThrowIfExpired(long now){if(started is long first&&now-first>=timeout)throw new TimeoutException("Desktop capture could not recover from a display transition within three seconds; restoring desktop.");}
 internal void FreshFrame(){started=null;}
}
internal sealed record DriverOperationResult(int ExitCode,string Output)
{
 internal static bool RestartRequired(int code)=>code is 3010 or 1641;
 internal string RequireSuccess(){if(RestartRequired(ExitCode))throw new DriverRestartRequiredException($"Windows requires a restart to finish the driver operation ({ExitCode}). {Output.Trim()}");if(ExitCode!=0)throw new InvalidOperationException($"Windows driver operation failed ({ExitCode}): {Output.Trim()}");return Output;}
}
internal sealed class DriverRestartRequiredException(string message):InvalidOperationException(message);
internal sealed record DriverRestartState(string? BootId,DateTime RequestedUtc,string Detail)
{
 internal static bool Pending(string? savedBoot,string? currentBoot,IReadOnlyList<int?> problemCodes)=>!(savedBoot is not null&&currentBoot is not null&&savedBoot!=currentBoot)&&!(problemCodes.Count>0&&problemCodes.All(code=>code==22));
}
internal static class DriverDisable
{
 internal static async Task RunAsync(IReadOnlyList<string> ids,Func<string,Task<DriverOperationResult>> run,Func<string,int?>? problemCode=null)
 {
  var errors=new List<Exception>();
  foreach(string id in ids)try{
   // Disabled devices can return ERROR_DEVICE_NOT_CONNECTED after reboot.
   // Verify the native state; absence or an unverified error is not success.
   if(problemCode?.Invoke(id)==22)continue;
   var result=await run(id);
   if(result.ExitCode==1167&&problemCode?.Invoke(id)==22)continue;
   result.RequireSuccess();
  }catch(Exception e){errors.Add(e);}
  if(errors.Count>0)throw new AggregateException("Driver disable did not finish for every managed instance.",errors);
 }
}
internal sealed record OwnedOutputRecoveryPlan(bool ReplayOriginal,IReadOnlyList<DisplaySnapshot> Detach);
internal static class OwnedOutputRecovery
{
 internal static List<string> Execute(OwnedOutputRecoveryPlan plan,IReadOnlyList<DisplaySnapshot> original,Action<DisplaySnapshot> stageOriginal,Action<DisplaySnapshot> detach,Action commit)
 {
  var errors=new List<string>();bool staged=false;
  if(plan.ReplayOriginal)foreach(var saved in original.OrderByDescending(s=>s.Primary))try{stageOriginal(saved);staged=true;}catch(Exception e){errors.Add(e.Message);}
  bool physicalModesStaged=plan.ReplayOriginal&&errors.Count==0;
  foreach(var output in plan.Detach){
   if(output.Primary&&!physicalModesStaged){errors.Add($"Managed primary {output.DeviceName} was left attached because its replacement physical mode could not be staged.");continue;}
   try{detach(output);staged=true;}catch(Exception e){errors.Add(e.Message);}
  }
  if(staged)try{commit();}catch(Exception e){errors.Add(e.Message);}
  return errors;
 }
 internal static OwnedOutputRecoveryPlan Plan(IReadOnlyList<DisplaySnapshot> original,IReadOnlyList<DisplaySnapshot> expected,IReadOnlyList<DisplaySnapshot> current,IReadOnlyList<string> ownedKeys)
 {
  bool SameIdentity(DisplaySnapshot a,DisplaySnapshot b)=>string.Equals(a.AdapterId,b.AdapterId,StringComparison.OrdinalIgnoreCase)&&string.Equals(a.DeviceKey,b.DeviceKey,StringComparison.OrdinalIgnoreCase);
  bool OwnedExtra(DisplaySnapshot c)=>!original.Any(o=>SameIdentity(o,c))&&string.Equals(c.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase)&&ownedKeys.Contains(c.DeviceKey,StringComparer.OrdinalIgnoreCase);
  // Removing the virtual primary can normalize physical coordinates first while
  // retaining our explicit output-refresh override. Accept only the saved geometry
  // at the original or planned Hz; unrelated mode/position edits still fail closed.
  bool KnownPhysical(DisplaySnapshot o,DisplaySnapshot c)=>c==o||expected.Any(e=>SameIdentity(e,o)&&(e==c||(e.Width==o.Width&&e.Height==o.Height&&c==o with{Hz=e.Hz})));
  // An interrupted restore may already have restored some physical modes and disabled
  // only one instance. Accept those states, but never replay over an unrelated edit.
  bool replay=original.Count>0&&original.All(o=>current.Any(c=>SameIdentity(o,c)&&KnownPhysical(o,c)))&&current.All(c=>original.Any(o=>SameIdentity(o,c))||OwnedExtra(c));
  bool visibleOutputRemains=current.Any(c=>!OwnedExtra(c));
  var detach=visibleOutputRemains?current.Where(c=>OwnedExtra(c)&&(replay||!c.Primary)).ToArray():[];
  return new(replay,detach);
 }
}
