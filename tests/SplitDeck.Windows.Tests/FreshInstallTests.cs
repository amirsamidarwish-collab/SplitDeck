using SplitDeck.Windows;
internal static class FreshInstallTests
{
 internal static async Task FailedRemovalKeepsOwnership()
 {
  bool owner=true,settings=true,registry=true,disabled=false;
  try{await FirstInstallRollback.RunAsync("new-device",_=>throw new IOException("Removal failed."),_=>{disabled=true;return Task.CompletedTask;},()=>{registry=false;return Task.CompletedTask;},()=>{owner=false;settings=false;return Task.CompletedTask;});throw new Exception("Expected cleanup failure.");}catch(AggregateException){}
  Check(owner&&settings&&registry&&disabled,"Failed removal must keep ownership/configuration and attempt to disable the surviving device.");
 }
 internal static async Task RemovedDeviceClearsAttempt()
 {
  var calls=new List<string>();
  await FirstInstallRollback.RunAsync("new-device",id=>{calls.Add("remove "+id);return Task.CompletedTask;},_=>throw new Exception("Removed device needs no disable."),()=>{calls.Add("registry");return Task.CompletedTask;},()=>{calls.Add("files");return Task.CompletedTask;});
  Check(calls.SequenceEqual(new[]{"remove new-device","registry","files"}),"Delete metadata only after device removal succeeds.");
  calls.Clear();await FirstInstallRollback.RunAsync(null,_=>throw new Exception("No device created."),_=>throw new Exception("No device created."),()=>{calls.Add("registry");return Task.CompletedTask;},()=>{calls.Add("files");return Task.CompletedTask;});Check(calls.Count==2,"An attempt before device creation may clear its own configuration.");
 }
 static void Check(bool value,string detail){if(!value)throw new Exception(detail);}
}
