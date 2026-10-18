namespace SplitDeck.Windows;

internal static class DriverStateTransition
{
 internal static async Task<DriverOperationResult> RunAsync(bool enable,Func<Task<DriverOperationResult>> utility,Func<bool,DriverOperationResult> native,Func<DriverHealthState> inspect,TimeSpan timeout)
 {
  if(!enable){var before=inspect();if(before.PackageVerified&&before.ProblemCode==22)return new(0,"The managed device is already disabled.");}
  var result=await utility();
  if(result.ExitCode==1167){
   var health=inspect();
   if(!enable&&health.PackageVerified&&health.ProblemCode==22)return new(0,"The managed device is verified disabled after disconnect.");
   if(health.PackageVerified&&health.ProblemCode==(enable?22:0))result=native(enable);
  }
  if(result.ExitCode!=0)return result;
  var deadline=DateTime.UtcNow+timeout;
  do{
   var health=inspect();
   if(health.PackageVerified&&health.ProblemCode==(enable?0:22))return result;
   if(health.RestartRequired)return new(3010,"The managed device requires a restart. "+health.Detail);
   if(DateTime.UtcNow>=deadline)throw new TimeoutException($"Windows did not confirm the managed device {(enable?"enabled":"disabled")} state. {health.Detail}");
   await Task.Delay(200);
  }while(true);
 }
}
