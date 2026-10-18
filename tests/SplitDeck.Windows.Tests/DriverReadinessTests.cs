using SplitDeck.Windows;

internal static class DriverReadinessTests
{
 internal static Task CachedReads()
 {
  int probes=0;
  var cache=new DriverReadinessCache(()=>{probes++;return true;});
  for(int i=0;i<500;i++)
  {
   var state=cache.State;
   Check(!state.Known&&!state.Ready&&!state.Checking,"Unverified readiness must remain unavailable.");
  }
  Check(probes==0,"Editing reads must never run driver checks.");
  return Task.CompletedTask;
 }

 internal static async Task SingleFlight()
 {
  using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int calls=0;
  var cache=new DriverReadinessCache(()=>{Interlocked.Increment(ref calls);entered.Set();Check(release.Wait(TimeSpan.FromSeconds(3)),"Probe release timed out.");return true;});
  Task first=cache.RefreshAsync();
  try
  {
   Check(entered.Wait(TimeSpan.FromSeconds(1)),"Background probe did not start.");
   Check(!first.IsCompleted&&cache.State.Checking&&!cache.State.Ready,"Slow probe must return before completion and disable Apply.");
   Task second=cache.RefreshAsync();
   Check(ReferenceEquals(first,second),"Concurrent refreshes must share one probe.");
   for(int i=0;i<500;i++)Check(cache.State.Checking&&!cache.State.Ready,"Editing must remain independent of a pending probe.");
   Check(calls==1,"Repeated reads or refreshes launched redundant checks.");
  }
  finally{release.Set();}
  await first.WaitAsync(TimeSpan.FromSeconds(2));
  Check(cache.State is {Known:true,Ready:true,Checking:false},"Successful readiness was not published.");
 }

 internal static async Task Invalidation()
 {
  using var firstEntered=new ManualResetEventSlim();using var firstRelease=new ManualResetEventSlim();
  using var secondEntered=new ManualResetEventSlim();using var secondRelease=new ManualResetEventSlim();int calls=0;
  var cache=new DriverReadinessCache(()=>
  {
   int call=Interlocked.Increment(ref calls);
   if(call==1){firstEntered.Set();Check(firstRelease.Wait(TimeSpan.FromSeconds(3)),"First release timed out.");return true;}
   secondEntered.Set();Check(secondRelease.Wait(TimeSpan.FromSeconds(3)),"Second release timed out.");return false;
  });
  Task pending=cache.RefreshAsync();
  try
  {
   Check(firstEntered.Wait(TimeSpan.FromSeconds(1)),"First probe did not start.");
   cache.Invalidate();firstRelease.Set();
   Check(secondEntered.Wait(TimeSpan.FromSeconds(1)),"Invalidation must retry the stale in-flight result.");
   Check(!cache.State.Known&&!cache.State.Ready&&cache.State.Checking,"Stale success must never enable Apply.");
  }
  finally{firstRelease.Set();secondRelease.Set();}
  await pending.WaitAsync(TimeSpan.FromSeconds(2));
  Check(cache.State is {Known:true,Ready:false,Checking:false},"Fresh failed readiness was not published.");
  cache.Invalidate();Check(cache.State is {Known:false,Ready:false,Checking:false},"Invalidation must immediately clear cached readiness.");
 }

 internal static async Task Errors()
 {
  int calls=0;var cache=new DriverReadinessCache(()=>++calls==1?throw new IOException("Trust verification unavailable"):true);
  await cache.RefreshAsync();
  Check(cache.State is {Known:true,Ready:false,Checking:false,Error:"Trust verification unavailable"},"Probe errors must fail closed without crashing or leaving refresh pending.");
  await cache.RefreshAsync();
  Check(cache.State is {Known:true,Ready:true,Checking:false,Error:null},"A later successful refresh must recover readiness.");
  cache.Invalidate();
  Check(cache.State is {Known:false,Ready:false,Checking:false},"Previously successful readiness must be unavailable immediately after invalidation.");
 }

 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
