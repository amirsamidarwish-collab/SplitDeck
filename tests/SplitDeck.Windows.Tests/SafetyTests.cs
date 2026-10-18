using SplitDeck.Windows;
internal static class SafetyTests
{
 internal static async Task Run()
 {
  var original=new DisplaySnapshot("A","id","key",0,0,1920,1080,60,true);var applied=original with{Y=-2000,Primary=false};
  Assert(TopologyGuard.CanRestore([original],[applied],[applied]),"owned topology");
  Assert(!TopologyGuard.CanRestore([original],[applied],[applied with{X=10}]),"external position");
  Assert(!TopologyGuard.CanRestore([original],[applied],[]),"unplug");
  Assert(!TopologyGuard.CanRestore([original],[applied],[applied with{DeviceKey="replacement"}]),"replacement");
  var log=new List<int>();var stack=new RecoveryStack();for(int i=0;i<4;i++){int step=i;stack.Push(()=>{log.Add(step);if(step==2)throw new IOException("fake failure");return Task.CompletedTask;});}try{await stack.RevertAsync();}catch(AggregateException){}Assert(log.SequenceEqual(new[]{3,2,1,0}),"all recovery actions");
  Assert(TopologyGuard.CanRestore([original],[applied],[original]),"already original after interrupted cleanup");
  var enabled=new[]{original,new DisplaySnapshot("V","virtual","vkey",1920,0,1280,720,60,false)};
  Assert(TopologyGuard.CanRestore([original],enabled,enabled),"partial enable before physical staging");
  for(int failure=0;failure<4;failure++){var calls=new List<int>();var cleanup=new RecoveryStack();for(int i=0;i<4;i++){int step=i;cleanup.Push(()=>{calls.Add(step);if(step==failure)throw new IOException("fake recovery failure");return Task.CompletedTask;});}try{await cleanup.RevertAsync();}catch(AggregateException){}Assert(calls.Count==4,"each failing recovery step continues");}
  try{await WatchdogReadiness.WaitAsync(()=>false,()=>true,TimeSpan.FromSeconds(1));throw new Exception("Expected early exit");}catch(IOException){}
  try{await WatchdogReadiness.WaitAsync(()=>false,()=>false,TimeSpan.FromMilliseconds(50));throw new Exception("Expected bounded timeout");}catch(IOException){}
  await WatchdogReadiness.WaitAsync(()=>true,()=>false,TimeSpan.FromSeconds(1));
  bool owner=false,device=false,configuration=false,healthy=false;
  Func<Task> rollback=()=>{device=false;configuration=false;owner=false;return Task.CompletedTask;};
  try{await InstallationAttempt.RunAsync(()=>{configuration=true;device=true;owner=true;throw new IOException("Fake driver bind failure");},rollback);}catch(IOException){}
  Assert(!owner&&!device&&!configuration&&!healthy,"failed install releases attempt resources");
  await InstallationAttempt.RunAsync(()=>{configuration=true;device=true;healthy=true;owner=true;return Task.CompletedTask;},rollback);
  Assert(owner&&device&&configuration&&healthy,"retry installation succeeds");
  int launches=0;
  await using(var controller=new SessionController((_,_)=>{launches++;throw new System.ComponentModel.Win32Exception(1223,"Fake UAC decline");},()=>{},TimeSpan.FromMilliseconds(100))){
   for(int i=0;i<2;i++){try{await controller.SetupDriverAsync();throw new Exception("Expected decline");}catch(System.ComponentModel.Win32Exception){}await controller.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(1));}Assert(launches==2,"retry after declined startup");
  }
  await using(var controller=new SessionController((_,_)=>System.Diagnostics.Process.GetCurrentProcess(),()=>{},TimeSpan.FromMilliseconds(100))){
   try{await controller.SetupDriverAsync();throw new Exception("Expected connection timeout");}catch(OperationCanceledException){}await controller.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(1));
  }
  var duringStartup=new SessionController((_,_)=>System.Diagnostics.Process.GetCurrentProcess(),()=>{},TimeSpan.FromSeconds(30));var pending=duringStartup.SetupDriverAsync();await Task.Delay(20);await duringStartup.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));try{await pending;throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
  var helpers=new List<Task>();int attemptNumber=0;
  var ipcController=new SessionController((args,_)=>{
   var request=System.Text.Json.JsonSerializer.Deserialize<HelperRequest>(File.ReadAllText(args[1]))!;int number=++attemptNumber;
   helpers.Add(Task.Run(async()=>{
    using var client=new System.IO.Pipes.NamedPipeClientStream(".",request.Pipe,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);await client.ConnectAsync(2000);
    var writer=new StreamWriter(client,new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(client,System.Text.Encoding.UTF8,false,4096,true);
    await writer.WriteLineAsync(request.Token);
    if(number==1){await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new HelperMessage("ERROR","Fake preparation failure")));return;}
    await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new HelperMessage("ACTIVE","fake active")));
    Assert(await reader.ReadLineAsync()=="STOP","stop reaches current attempt");
    await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new HelperMessage("RESTORED","fake restored")));
   }));return System.Diagnostics.Process.GetCurrentProcess();
  },()=>{},TimeSpan.FromSeconds(2));
  try{await ipcController.SetupDriverAsync();throw new Exception("Expected preparation failure");}catch(InvalidOperationException){}
  await ipcController.SetupDriverAsync();Assert(ipcController.IsActive,"retry has independent live pipe");await ipcController.RestoreAsync();await ipcController.DisposeAsync();await Task.WhenAll(helpers);
  foreach(bool restoredFirst in new[]{false,true}){
   Task helper=Task.CompletedTask;
   var failedController=new SessionController((args,_)=>{
    var request=System.Text.Json.JsonSerializer.Deserialize<HelperRequest>(File.ReadAllText(args[1]))!;
    helper=Task.Run(async()=>{
     using var client=new System.IO.Pipes.NamedPipeClientStream(".",request.Pipe,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);await client.ConnectAsync(2000);
     using var writer=new StreamWriter(client,new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true};
     await writer.WriteLineAsync(request.Token);
     if(restoredFirst)await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new HelperMessage("RESTORED","fake restored")));
     await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new HelperMessage("ERROR","Fake preparation failure")));
    });return System.Diagnostics.Process.GetCurrentProcess();
   },()=>{},TimeSpan.FromSeconds(2));
   try{await failedController.SetupDriverAsync();throw new Exception("Expected preparation failure");}catch(InvalidOperationException){}
   if(restoredFirst)await failedController.RestoreAsync();else try{await failedController.RestoreAsync();throw new Exception("Expected unconfirmed recovery error");}catch(InvalidOperationException){}
   await failedController.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
   await failedController.DisposeAsync();await helper;Assert(!failedController.IsActive,"failed operation can close promptly");
  }
  using var lease=await MachineLease.AcquireAsync(TimeSpan.FromSeconds(1),"SplitDeck-test-"+Environment.ProcessId);
  try{using var second=await MachineLease.AcquireAsync(TimeSpan.FromMilliseconds(100),"SplitDeck-test-"+Environment.ProcessId);throw new Exception("Lease accepted a competing client");}catch(TimeoutException){}
 }
 static void Assert(bool value,string name){if(!value)throw new Exception(name);}
}

