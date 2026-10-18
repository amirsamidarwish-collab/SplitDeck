using SplitDeck.Core;
namespace SplitDeck.Windows;
internal readonly record struct ProjectedCursor(float X,float Y,float Width,float Height,float SourceU,float SourceV,float SourceWidth,float SourceHeight);
internal static class CursorProjection
{
 internal static ProjectedCursor Project(MonitorInfo source,PixelRect bounds,int x,int y,int width,int height,int hotX,int hotY)
 {
  float left=x-source.X-hotX,top=y-source.Y-hotY;
  return new(bounds.X+left*bounds.Width/source.Width,bounds.Y+top*bounds.Height/source.Height,width*bounds.Width/(float)source.Width,height*bounds.Height/(float)source.Height,left/source.Width,top/source.Height,width/(float)source.Width,height/(float)source.Height);
 }
 internal static bool Visible(uint flags)=>(flags&1)!=0&&(flags&2)==0;
}
internal sealed class CursorShapeCache<T> where T:class
{
 private nint currentHandle;private T? current;
 internal T Get(nint handle,Func<nint,T> create,Action<T> release)
 {
  if(current is not null&&currentHandle==handle)return current;
  var replacement=create(handle);var old=current;current=replacement;currentHandle=handle;if(old is not null)release(old);return replacement;
 }
 internal void Clear(Action<T> release){var old=current;current=null;currentHandle=0;if(old is not null)release(old);}
}
internal sealed class PresentationCadence(long ticksPerSecond,int rate)
{
 private readonly long period=Math.Max(1,ticksPerSecond/Math.Clamp(rate,1,1000));private long? next;
 internal long WaitTicks(long now)
 {
  long due=next??now;
  if(now>=due){next=due+((now-due)/period+1)*period;return 0;}
  next=due+period;return due-now;
 }
}
