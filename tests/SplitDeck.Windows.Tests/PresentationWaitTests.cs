using SplitDeck.Windows;
internal static class PresentationWaitTests
{
 internal static Task FractionalDeadlines()
 {
  foreach(int rate in new[]{60,120,144}){
   const long frequency=1_000_000;var presenter=new PresentationCadence(frequency,rate);var capture=new CaptureCadence(frequency,rate);long now=0,period=frequency/rate;int received=0,early=0;
   for(int i=0;i<rate*2;i++){
    long remaining=presenter.WaitTicks(now);if(remaining>0)now+=(long)PresentationWait.Milliseconds(remaining,frequency)*1000;
    if(now<i*period)early++;if(capture.Due(now))received++;now+=80+i%3*10;
   }
   if(early!=0||received<rate*2-1)throw new Exception($"{rate}Hz fractional waits woke early{early} times; capture due on{received}/{rate*2} ticks.");
  }
  if(PresentationWait.Milliseconds(1,1_000_000)!=1||PresentationWait.Milliseconds(0,1_000_000)!=0)throw new Exception("A positive sub-millisecond wait must not spin or undersleep.");
  return Task.CompletedTask;
 }
}
