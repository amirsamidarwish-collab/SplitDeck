using SplitDeck.Core;
namespace SplitDeck.Windows;

internal static class OutputRefreshPolicy
{
 internal static int? Select(int requested,IReadOnlyList<VirtualScreenLayout> layouts,double currentPanelHz)
 {
  if(requested is <0 or >1000||!double.IsFinite(currentPanelHz)||currentPanelHz<=0||layouts.Count is not(2 or 3)||layouts.Any(l=>l.RefreshHz<=0))throw new ArgumentException("Valid output refresh and active virtual-screen modes are required.");
  if(requested==0)return null;
  if(requested>currentPanelHz||requested<layouts.Max(l=>l.RefreshHz))throw new ArgumentOutOfRangeException(nameof(requested),"Output refresh must be at least every active virtual-screen rate and cannot exceed the current physical refresh.");
  return requested;
 }
 internal static IReadOnlyList<DisplaySnapshot> PhysicalModes(IReadOnlyList<DisplaySnapshot> original,string selectedDevice,int shift,int? outputHz)
 {
  if(original.Count(m=>string.Equals(m.DeviceName,selectedDevice,StringComparison.OrdinalIgnoreCase))!=1||outputHz is <1 or >1000)throw new ArgumentException("One selected physical output and a valid optional refresh are required.");
  return original.Select(m=>m with{Y=checked(m.Y-shift),Primary=false,Hz=outputHz is int hz&&string.Equals(m.DeviceName,selectedDevice,StringComparison.OrdinalIgnoreCase)?hz:m.Hz}).ToArray();
 }
}
