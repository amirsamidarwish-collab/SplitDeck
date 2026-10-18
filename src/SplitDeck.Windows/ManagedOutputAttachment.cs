using SplitDeck.Core;
namespace SplitDeck.Windows;

internal sealed record DisplayAdapterState(string DeviceName,string AdapterId,string DeviceKey,bool Attached);
internal sealed record OutputAttachment(string DeviceName,string DeviceKey,int X,int Y,int Width,int Height,int Hz);
internal static class ManagedOutputAttachment
{
 internal static IReadOnlyList<OutputAttachment> Plan(IReadOnlyList<DisplayAdapterState> adapters,IReadOnlyList<string> ownedKeys,IReadOnlyList<VirtualScreenLayout> layouts,DisplaySnapshot anchor)
 {
  if(layouts.Count is not(2 or 3)||ownedKeys.Count!=layouts.Count||ownedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=layouts.Count)return [];
  var matches=ownedKeys.Select(key=>adapters.Where(a=>string.Equals(a.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase)&&string.Equals(a.DeviceKey,key,StringComparison.OrdinalIgnoreCase)).ToArray()).ToArray();
  if(matches.Any(a=>a.Length!=1))return [];
  var ordered=matches.Select(a=>a[0]).OrderBy(a=>a.DeviceName,StringComparer.Ordinal).ToArray();
  if(ordered.Select(a=>a.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=layouts.Count)return [];
  var result=new List<OutputAttachment>();int x=checked(anchor.X+anchor.Width);
  for(int i=0;i<ordered.Length;i++)if(!ordered[i].Attached){var layout=layouts[i];result.Add(new(ordered[i].DeviceName,ordered[i].DeviceKey,x,anchor.Y,layout.RenderWidth,layout.RenderHeight,layout.RefreshHz));x=checked(x+layout.RenderWidth);}
  return result;
 }
}
