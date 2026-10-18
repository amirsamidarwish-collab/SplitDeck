namespace SplitDeck.Core;

public sealed record LayoutDemand(double MegapixelsPerSecond,double WorkloadRatio,double BufferMiB,double BufferRatio);
public static class LayoutDemandEstimator
{
 public static LayoutDemand Estimate(IReadOnlyList<VirtualScreenLayout> screens,int panelWidth,int panelHeight,double panelHz)
 {
  ArgumentNullException.ThrowIfNull(screens);
  if(panelWidth is <1 or >16384||panelHeight is <1 or >16384||!double.IsFinite(panelHz)||panelHz<=0||screens.Count is not(2 or 3)||screens.Any(s=>s.RenderWidth is <1 or >8192||s.RenderHeight is <1 or >8192||s.RefreshHz<=0||s.RefreshHz>panelHz))throw new ArgumentException("A valid panel and two or three supported screen modes are required.");
  double panelPixels=(double)panelWidth*panelHeight;
  double sourcePixels=screens.Sum(s=>(double)s.RenderWidth*s.RenderHeight);
  double pixelsPerSecond=screens.Sum(s=>(double)s.RenderWidth*s.RenderHeight*s.RefreshHz)+panelPixels*screens.Max(s=>s.RefreshHz);
  // Fixed, explicit comparison model, not a calibrated hardware-capacity model.
  double bufferBytes=(sourcePixels*2+panelPixels*2)*4;
  return new(pixelsPerSecond/1e6,pixelsPerSecond/(panelPixels*panelHz*2),bufferBytes/1048576,bufferBytes/(panelPixels*16));
 }
}
