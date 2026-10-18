using SplitDeck.Windows;

internal static class PacedCaptureTests
{
 internal static Task MixedRates()
 {
  const long frequency=1_000_000;
  foreach(int pollRate in new[]{60,120,144}){
   var fast=new CaptureCadence(frequency,144);var slow=new CaptureCadence(frequency,30);int fastCalls=0,slowCalls=0;
   // Vsync and cursor work shift capture relative to the presenter's absolute
   // deadline. The presenter already limits calls to the requested poll rate.
   for(int i=0;i<pollRate;i++){
    long now=i*frequency/pollRate+(i%2==0?600:0);
    if(fast.Due(now,pollRate))fastCalls++;
    if(slow.Due(now,pollRate))slowCalls++;
   }
   if(fastCalls!=pollRate)throw new Exception($"A paced {pollRate}Hz loop skipped {pollRate-fastCalls} fast-source opportunities.");
   if(slowCalls<29||slowCalls>30)throw new Exception($"The 30Hz source exceeded its independent budget: {slowCalls}.");
  }
  var unpaced=new CaptureCadence(frequency,144);
  if(!unpaced.Due(0)||unpaced.Due(100))throw new Exception("Standalone capture must retain its own rate limit.");
  return Task.CompletedTask;
 }
}
