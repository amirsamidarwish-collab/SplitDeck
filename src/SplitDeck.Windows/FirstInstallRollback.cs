namespace SplitDeck.Windows;
internal static class FirstInstallRollback
{
 internal static async Task RunAsync(string? created,Func<string,Task> remove,Func<string,Task> disable,Func<Task> clearConfiguration,Func<Task> clearOwnershipAndSettings)
 {
  if(created is not null){
   try{await remove(created);}
   catch(Exception removal){
    var errors=new List<Exception>{removal};try{await disable(created);}catch(Exception disabling){errors.Add(disabling);}
    throw new AggregateException("The new driver device could not be removed. Its ownership and configuration were retained for cleanup before retry.",errors);
   }
  }
  var cleanup=new RecoveryStack();cleanup.Push(clearOwnershipAndSettings);cleanup.Push(clearConfiguration);await cleanup.RevertAsync();
 }
}
