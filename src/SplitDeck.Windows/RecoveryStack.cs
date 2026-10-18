namespace SplitDeck.Windows;
public sealed class RecoveryStack
{
 private readonly Stack<Func<Task>> actions=new();private readonly object gate=new();private Task? reverting;
 public void Push(Func<Task> restore){ArgumentNullException.ThrowIfNull(restore);lock(gate){if(reverting is not null)throw new InvalidOperationException("Recovery already started.");actions.Push(restore);}}
 public Task RevertAsync(){lock(gate)return reverting??=RunAsync();}
 private async Task RunAsync(){var errors=new List<Exception>();while(actions.TryPop(out var action)){try{await action();}catch(Exception e){errors.Add(e);}}if(errors.Count>0)throw new AggregateException("Some recovery steps failed.",errors);}
}
