using SplitDeck.Windows;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
internal static class ControllerRecoveryTests
{
 internal static async Task ActivationErrorAfterRestore()
 {
  foreach(bool confirmed in new[]{true,false}){
   Task helper=Task.CompletedTask;var status=new List<string>();
   await using var controller=new SessionController((args,_)=>{
    var request=JsonSerializer.Deserialize<HelperRequest>(File.ReadAllText(args[1]))!;
    helper=Task.Run(async()=>{
     using var client=new NamedPipeClientStream(".",request.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(2000);
     using var writer=new StreamWriter(client,new UTF8Encoding(false),4096,true){AutoFlush=true};await writer.WriteLineAsync(request.Token);
     if(confirmed)await writer.WriteLineAsync(JsonSerializer.Serialize(new HelperMessage("RESTORED","Original desktop restored.")));
     await writer.WriteLineAsync(JsonSerializer.Serialize(new HelperMessage("ERROR","Activation topology mismatch.")));
    });return Process.GetCurrentProcess();
   },()=>{},TimeSpan.FromSeconds(2));
   controller.StatusChanged+=status.Add;
   try{await controller.SetupDriverAsync();throw new Exception("Activation failure must remain an operation error.");}catch(InvalidOperationException error){Check(error.Message.Contains("Activation topology mismatch",StringComparison.Ordinal),"Retain the original activation error.");}
   if(confirmed)await controller.RestoreAsync();
   else try{await controller.RestoreAsync();throw new Exception("Without RESTORED recovery must remain unconfirmed.");}catch(InvalidOperationException error){Check(error.Message.Contains("Recovery was not confirmed",StringComparison.Ordinal),"Missing restore acknowledgement must fail closed.");}
   Check(status.Any(s=>s.Contains("Activation topology mismatch",StringComparison.Ordinal)),"Confirmed recovery must not erase the activation failure status.");await helper;
  }
 }
 static void Check(bool value,string detail){if(!value)throw new Exception(detail);}
}
