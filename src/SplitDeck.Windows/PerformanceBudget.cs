namespace SplitDeck.Windows;

internal static class PerformanceBudget
{
 internal static bool PresentationHealthy(double? framesPerSecond,double target)=>framesPerSecond is double fps&&double.IsFinite(fps)&&target>0&&fps>=target*0.8;
 internal static double CpuPercent(ulong previousIdle,ulong previousTotal,ulong idle,ulong total)
 {
  if(total<=previousTotal||idle<previousIdle)return 0;
  return Math.Clamp(100.0*(1-(idle-previousIdle)/(double)(total-previousTotal)),0,100);
 }
 internal static string? Excess(double baselineCpu,double cpu,long availableMiB,double? baselineGpu,double? gpu)
 {
  if(availableMiB<512)return "Available system memory fell below 512 MiB.";
  if(cpu>Math.Min(90,baselineCpu+10))return "Total CPU usage exceeded the baseline by more than 10 percentage points.";
  if(gpu is double current&&current>Math.Min(90,(baselineGpu??75)+15))return "GPU activity exceeded the test's overhead budget.";
  return null;
 }
}
