namespace SplitDeck.Windows;
internal static class PresentationWait
{
 internal static int Milliseconds(long remainingTicks,long frequency)
 {
  if(frequency<=0)throw new ArgumentOutOfRangeException(nameof(frequency));
  if(remainingTicks<=0)return 0;
  // WaitOne(TimeSpan) truncates milliseconds. Round upward so paired capture
  // clocks do not see a poll before the intended presentation deadline.
  return (int)Math.Min(int.MaxValue,Math.Ceiling(remainingTicks*1000d/frequency));
 }
}
