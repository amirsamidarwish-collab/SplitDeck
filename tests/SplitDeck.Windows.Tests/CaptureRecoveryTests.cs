using SplitDeck.Windows;

internal static class CaptureRecoveryTests
{
 internal static Task AccessLostPolicy()
 {
  var recovery=new DuplicationRecovery(1000);
  Check(DuplicationRecovery.IsRecoverable(unchecked((int)0x887A0026)),"access loss is recoverable");
  foreach(int hr in new[]{unchecked((int)0x887A0005),unchecked((int)0x887A0006),unchecked((int)0x80070005),unchecked((int)0x887A0022)})Check(!DuplicationRecovery.IsRecoverable(hr),"device/security failures remain fatal");
  recovery.Begin(0);Check(recovery.ShouldAttempt(0),"first retry is immediate");
  Check(!recovery.ShouldAttempt(50),"retry is throttled");Check(recovery.ShouldAttempt(100),"retry after backoff");
  recovery.Begin(2000); // Repeated ACCESS_LOST must not extend the stale-image deadline.
  try{recovery.ThrowIfExpired(3000);throw new Exception("Expected bounded stale capture timeout.");}catch(TimeoutException){}
  recovery.FreshFrame();recovery.ThrowIfExpired(4000);recovery.Begin(5000);Check(recovery.ShouldAttempt(5000),"fresh frame resets retry budget");
  return Task.CompletedTask;
 }
 internal static async Task DisableRestart()
 {
  var attempts=new List<string>();
  try{await DriverDisable.RunAsync(["first","second"],id=>{attempts.Add(id);return Task.FromResult(id=="first"?new DriverOperationResult(3010,"restart"):new DriverOperationResult(0,"disabled"));});throw new Exception("Expected restart-required result.");}
  catch(AggregateException e){Check(e.Flatten().InnerExceptions.OfType<DriverRestartRequiredException>().Count()==1,"3010 classified as restart");}
  Check(attempts.SequenceEqual(new[]{"first","second"}),"all devices attempted after restart result");
  attempts.Clear();
  try{await DriverDisable.RunAsync(["first","second"],id=>{attempts.Add(id);return id=="first"?Task.FromException<DriverOperationResult>(new IOException("utility failed")):Task.FromResult(new DriverOperationResult(0,"disabled"));});throw new Exception("Expected utility failure.");}catch(AggregateException){}
  Check(attempts.Count==2,"all devices attempted after hard failure");
  Check(DriverOperationResult.RestartRequired(3010)&&DriverOperationResult.RestartRequired(1641)&&!DriverOperationResult.RestartRequired(5),"Windows restart codes recognized");
  Check(DriverRestartState.Pending("boot-a","boot-a",[0,22]),"same boot active instance remains restart-blocked");
  Check(!DriverRestartState.Pending("boot-a","boot-b",[0,22]),"later boot permits verification retry");
  Check(!DriverRestartState.Pending("boot-a","boot-a",[22,22]),"verified disabled pair clears pending restart");
  Check(DriverRestartState.Pending(null,null,[0,22])&&DriverRestartState.Pending("boot-a","boot-a",[null,22]),"missing boot or device data stays blocked");
 }
 internal static Task PartialRestore()
 {
  var physical=new DisplaySnapshot("P","PCI\\gpu","physical",0,0,2560,1080,144,true);
  var moved=physical with{Y=-1080,Primary=false};
  var first=new DisplaySnapshot("V1","Root\\MttVDD","owned-first",0,0,1920,1080,144,true);
  var second=new DisplaySnapshot("V2","Root\\MttVDD","owned-second",1920,0,603,1072,30,false);
  string[] keys=[first.DeviceKey,second.DeviceKey];
  var partial=second with{X=2560};
  var plan=OwnedOutputRecovery.Plan([physical],[moved,first,second],[physical,partial],keys);
  Check(plan.ReplayOriginal&&plan.Detach.SequenceEqual(new[]{partial}),"physical restored + remaining owned virtual is recoverable");
  Check(OwnedOutputRecovery.Plan([physical],[moved,first,second],[moved,first,second],keys).ReplayOriginal,"active owned topology can restore");
  Check(OwnedOutputRecovery.Plan([physical],[moved,first,second],[physical],keys).ReplayOriginal,"already original");
  var changed=physical with{Hz=60};
  plan=OwnedOutputRecovery.Plan([physical],[moved,first,second],[changed,partial],keys);
  Check(!plan.ReplayOriginal&&plan.Detach.Count==1,"external physical mode remains untouched while own nonprimary output can detach");
  var foreign=second with{DeviceKey="foreign"};
  plan=OwnedOutputRecovery.Plan([physical],[moved,first,second],[physical,foreign],keys);
  Check(!plan.ReplayOriginal&&plan.Detach.Count==0,"foreign virtual is untouched");
  plan=OwnedOutputRecovery.Plan([physical],[moved,first,second],[partial],keys);
  Check(!plan.ReplayOriginal&&plan.Detach.Count==0,"do not detach last display when physical disconnected");
  plan=OwnedOutputRecovery.Plan([physical],[moved,first,second],[physical,partial],[]);
  Check(!plan.ReplayOriginal&&plan.Detach.Count==0,"journal identity alone cannot authorize device changes");
  var detach=DisplayService.DetachMode(partial);
  Check(detach.Width==0&&detach.Height==0&&detach.Fields==(Native.DmPosition|Native.DmWidth|Native.DmHeight),"detach uses zero mode and explicit position/size fields");
  var detached=new List<DisplaySnapshot>();int commits=0;
  var activePlan=OwnedOutputRecovery.Plan([physical],[moved,first,second],[moved,first,second],keys);
  var errors=OwnedOutputRecovery.Execute(activePlan,[physical],_=>throw new IOException("physical mode rejected"),d=>detached.Add(d),()=>commits++);
  Check(errors.Count>0&&!detached.Contains(first)&&detached.Contains(second),"failed replacement physical mode must keep virtual primary attached");
  Check(commits==1,"other safe detach still commits");
  return Task.CompletedTask;
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
