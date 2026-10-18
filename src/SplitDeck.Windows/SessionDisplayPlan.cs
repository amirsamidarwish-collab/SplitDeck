using SplitDeck.Core;
namespace SplitDeck.Windows;

internal sealed record SessionDisplayMode(string DeviceName,int X,int Y,int Width,int Height,int Hz,bool Primary);
internal static class SessionDisplayPlan
{
 internal static IReadOnlyList<DisplaySnapshot> PhysicalModes(IReadOnlyList<DisplaySnapshot> original,string selectedDevice,IReadOnlyList<SessionDisplayMode> virtualModes,int? outputHz)
 {
  if(original.Count==0||original.Any(m=>m.Width<=0||m.Height<=0)||virtualModes.Count is not(2 or 3)||virtualModes.Count(m=>m.Primary)!=1)throw new ArgumentException("Physical outputs and exactly one virtual primary are required.");
  var primary=virtualModes.Single(m=>m.Primary);
  if(virtualModes.Any(m=>m.Y<primary.Y))throw new ArgumentException("Virtual sources must share the primary top edge.");
  // Windows requires a connected desktop. Keep the original physical group intact,
  // with its bottommost monitor sharing an edge with the virtual primary.
  var anchor=original.OrderByDescending(m=>(long)m.Y+m.Height).ThenByDescending(m=>string.Equals(m.DeviceName,selectedDevice,StringComparison.OrdinalIgnoreCase)).First();
  int shift=checked(anchor.Y+anchor.Height-primary.Y),offsetX=checked(primary.X-anchor.X);
  return OutputRefreshPolicy.PhysicalModes(original,selectedDevice,shift,outputHz).Select(m=>m with{X=checked(m.X+offsetX)}).ToArray();
 }
 internal static IReadOnlyList<SessionDisplayMode> Build(IReadOnlyList<MonitorInfo> monitors,IReadOnlyList<VirtualScreenLayout> layouts,int primaryIndex)
 {
  if(layouts.Count is not(2 or 3)||monitors.Count!=layouts.Count||primaryIndex<0||primaryIndex>=layouts.Count||monitors.Select(m=>m.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=monitors.Count||monitors.Any(m=>!m.IsVirtual)||layouts.Any(l=>l.RenderWidth<=0||l.RenderHeight<=0))throw new ArgumentException("Distinct virtual outputs, valid layouts and an in-range primary screen are required.");
  var result=new List<SessionDisplayMode>();int x=-checked(layouts.Take(primaryIndex).Sum(l=>l.RenderWidth));
  for(int i=0;i<layouts.Count;i++){var layout=layouts[i];result.Add(new(monitors[i].DeviceName,x,0,layout.RenderWidth,layout.RenderHeight,layout.RefreshHz,i==primaryIndex));x=checked(x+layout.RenderWidth);}
  return result;
 }
}
internal static class DriverActivation
{
 internal static async Task RunAsync(IReadOnlyList<string> owned,int count,Func<string,bool,Task> change)
 {
  if(count is not(2 or 3)||owned.Count<count||owned.Count>3||owned.Any(string.IsNullOrWhiteSpace)||owned.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=owned.Count)throw new ArgumentException("Enough distinct owned driver instances are required before activation.");
  foreach(string id in owned.Skip(count))await change(id,false);
  foreach(string id in owned.Take(count))await change(id,true);
 }
}
