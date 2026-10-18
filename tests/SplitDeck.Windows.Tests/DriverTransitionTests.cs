using SplitDeck.Windows;

internal static class DriverTransitionTests
{
 internal static async Task DisabledIdempotence()
 {
  int calls=0;
  var result=await DriverStateTransition.RunAsync(false,()=>{calls++;return Task.FromResult(new DriverOperationResult(1167,"disconnected"));},_=>throw new Exception("Disabled instance must not use native fallback"),()=>new(true,22,"disabled"),TimeSpan.Zero);
  Check(result.ExitCode==0&&calls==0,"Already-disabled instance must survive raw upgrade cleanup without a redundant disable.");
  int state=0;
  result=await DriverStateTransition.RunAsync(false,()=>{state=22;return Task.FromResult(new DriverOperationResult(1167,"disconnected during operation"));},_=>throw new Exception("Confirmed disabled race must not use native fallback"),()=>new(true,state,"verified"),TimeSpan.Zero);
  Check(result.ExitCode==0,"A fresh disabled state must settle a disconnect race in every call path.");
 }
 internal static async Task DisconnectedFallback()
 {
  foreach(bool enable in new[]{true,false}){
   int state=enable?22:0,calls=0;
   var result=await DriverStateTransition.RunAsync(enable,()=>Task.FromResult(new DriverOperationResult(1167,"disconnected")),requested=>{Check(requested==enable,"Wrong transition.");calls++;state=enable?0:22;return new(0,"native succeeded");},()=>new(true,state,"verified"),TimeSpan.Zero);
   Check(result.ExitCode==0&&calls==1,"A verified disconnected legacy device must use the class installer.");
  }
 }
 internal static async Task FailureBoundaries()
 {
  foreach(var health in new[]{new DriverHealthState(false,22,"untrusted"),new(true,null,"missing"),new(true,14,"restart"),new(true,52,"signature failure")}){
   int calls=0;var result=await DriverStateTransition.RunAsync(true,()=>Task.FromResult(new DriverOperationResult(1167,"disconnected")),_=>{calls++;return new(0,"");},()=>health,TimeSpan.Zero);
   Check(result.ExitCode==1167&&calls==0,"Missing, untrusted or unhealthy devices must not use fallback.");
  }
  foreach(int code in new[]{5,3010,1641}){
   int calls=0;var result=await DriverStateTransition.RunAsync(true,()=>Task.FromResult(new DriverOperationResult(code,"failure")),_=>{calls++;return new(0,"");},()=>new(true,22,"verified"),TimeSpan.Zero);
   Check(result.ExitCode==code&&calls==0,"Other failures and restarts must be preserved.");
  }
  var pending=await DriverStateTransition.RunAsync(true,()=>Task.FromResult(new DriverOperationResult(1167,"disconnected")),_=>new(3010,"native needs restart"),()=>new(true,22,"verified"),TimeSpan.Zero);
  Check(pending.ExitCode==3010,"Native restart must not become success.");
 }
 internal static async Task StateVerification()
 {
  foreach(bool enable in new[]{true,false}){
   try{
    await DriverStateTransition.RunAsync(enable,()=>Task.FromResult(new DriverOperationResult(0,"utility says success")),_=>throw new Exception("Must not fallback on success"),()=>new(true,enable?22:0,"unchanged"),TimeSpan.Zero);
    throw new Exception("Transition reporting success without requested native state must time out.");
   }catch(TimeoutException){}
  }
  int probes=0;
  var result=await DriverStateTransition.RunAsync(true,()=>Task.FromResult(new DriverOperationResult(0,"started")),_=>throw new Exception("Unexpected native fallback"),()=>new(true,++probes==1?22:0,"starting"),TimeSpan.FromSeconds(2));
  Check(result.ExitCode==0&&probes>=2,"Enable must wait past a transient disabled state.");
 }
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
