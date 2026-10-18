using SplitDeck.Windows;

internal static class DriverDisableTests
{
 internal static async Task AlreadyDisabled()
 {
  var calls=new List<string>();
  await DriverDisable.RunAsync(["first","second"],id=>{calls.Add(id);return Task.FromResult(new DriverOperationResult(1167,"not connected"));},_=>22);
  Check(calls.Count==0,"Native disabled devices must not be disabled again.");
  await DriverDisable.RunAsync(["first","second"],id=>{calls.Add(id);return Task.FromResult(new DriverOperationResult(0,"disabled"));},id=>id=="first"?22:0);
  Check(calls.SequenceEqual(new[]{"second"}),"A partially disabled pair must still disable its active instance.");
 }
 internal static async Task DisconnectedRace()
 {
  int probes=0;
  await DriverDisable.RunAsync(["race"],_=>Task.FromResult(new DriverOperationResult(1167,"not connected")),_=>++probes==1?0:22);
  Check(probes==2,"A disconnect result must be followed by native disabled-state verification.");
  foreach(int? state in new int?[]{0,null,14}){
   var calls=new List<string>();
   try{
    await DriverDisable.RunAsync(["first","second"],id=>{calls.Add(id);return Task.FromResult(new DriverOperationResult(1167,"not connected"));},_=>state);
    throw new Exception("Unverified disconnect must remain a recovery failure.");
   }catch(AggregateException e){Check(e.Flatten().InnerExceptions.Count==2,"Every unverified failure must be retained.");}
   Check(calls.Count==2,"Failure of one instance must not skip the other.");
  }
  foreach(int code in new[]{5,3010}){
   probes=0;
   try{
    await DriverDisable.RunAsync(["failure"],_=>Task.FromResult(new DriverOperationResult(code,"failure")),_=>++probes==1?0:22);
    throw new Exception("Other Windows errors must not be hidden by the disconnect exception.");
   }catch(AggregateException e){Check(code!=3010||e.Flatten().InnerExceptions.Single() is DriverRestartRequiredException,"Restart must remain distinct.");}
  }
 }
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
