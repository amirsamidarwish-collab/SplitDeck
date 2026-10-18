namespace SplitDeck.Windows;
internal static class TopologyGuard
{
 internal static bool CanRestore(IReadOnlyList<DisplaySnapshot> original,IReadOnlyList<DisplaySnapshot> expected,IReadOnlyList<DisplaySnapshot> current)
 {
  // Include all outputs: a newly attached monitor is an external topology change too.
  if(current.Count==original.Count&&original.All(current.Contains))return true;
  return current.Count==expected.Count&&expected.All(e=>current.Contains(e))&&original.All(o=>current.Any(c=>c.AdapterId==o.AdapterId&&c.DeviceKey==o.DeviceKey));
 }
}
internal sealed class MachineLease:IDisposable
{
 private readonly FileStream stream;private MachineLease(FileStream stream)=>this.stream=stream;
 internal static async Task<MachineLease> AcquireAsync(TimeSpan timeout,string? testName=null)
 {
  string folder=testName is null?DriverInstallation.Root:Path.GetTempPath();Directory.CreateDirectory(folder);
  string path=Path.Combine(folder,(testName??"machine")+".lock");var deadline=DateTime.UtcNow+timeout;
  do{try{return new(new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}catch(IOException){await Task.Delay(25);}}while(DateTime.UtcNow<deadline);
  throw new TimeoutException("Another SplitDeck setup, session, or recovery owns the driver. Restore that session and retry.");
 }
 public void Dispose()=>stream.Dispose();
}

internal static class InstallationAttempt
{
 internal static async Task RunAsync(Func<Task> install,Func<Task> rollback)
 {
  try{await install();}catch(Exception failure){try{await rollback();}catch(Exception cleanup){throw new AggregateException("Installation failed; cleanup needs attention before retry.",failure,cleanup);}throw;}
 }
}

internal static class WatchdogReadiness
{
 internal static async Task WaitAsync(Func<bool> ready,Func<bool> exited,TimeSpan timeout)
 {
  var end=DateTime.UtcNow+timeout;
  while(!ready()){if(exited()||DateTime.UtcNow>=end)throw new IOException("Recovery watchdog did not become ready. No display changes were made.");await Task.Delay(25);}
 }
}
