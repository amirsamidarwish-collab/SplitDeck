namespace SplitDeck.Windows;

internal sealed record DriverReadiness(bool Known, bool Ready, bool Checking, string? Error = null);

internal sealed class DriverReadinessCache(Func<bool> probe)
{
 private DriverReadiness state=new(false,false,false);
 private readonly object gate=new();
 private Task? refresh;
 private long generation;
 internal DriverReadiness State => Volatile.Read(ref state);
 internal Task RefreshAsync()
 {
  lock(gate)
  {
   if(refresh is not null)return refresh;
   Volatile.Write(ref state,new(false,false,true));
   return refresh=Task.Run(ProbeUntilCurrent);
  }
 }
 internal void Invalidate()
 {
  lock(gate)
  {
   generation++;
   Volatile.Write(ref state,new(false,false,refresh is not null));
  }
 }
 private void ProbeUntilCurrent()
 {
  while(true)
  {
   long started;lock(gate)started=generation;
   bool ready=false;string? error=null;
   try{ready=probe();}catch(Exception ex){error=ex.Message;}
   lock(gate)
   {
    // Setup/session changes can invalidate an in-flight answer. Never publish it.
    if(started!=generation)continue;
    Volatile.Write(ref state,new(true,ready,false,error));refresh=null;return;
   }
  }
 }
}
