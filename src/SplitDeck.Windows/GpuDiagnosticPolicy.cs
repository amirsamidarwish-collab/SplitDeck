namespace SplitDeck.Windows;

internal enum GpuQueryReadState{Pending,Ready,Unavailable}
internal static class GpuDiagnosticPolicy
{
 internal static GpuQueryReadState QueryState(int result)=>result==0?GpuQueryReadState.Ready:result<0?GpuQueryReadState.Unavailable:GpuQueryReadState.Pending;
 internal static bool ShouldSample(long frame)=>frame>0&&frame%16==0;
 internal static bool TryMilliseconds(ulong start,ulong end,ulong frequency,bool disjoint,out double milliseconds){milliseconds=0;if(disjoint||frequency==0||end<start)return false;milliseconds=(end-start)*1000.0/frequency;return double.IsFinite(milliseconds);}
 internal static long PixelArea(ReadOnlySpan<Vortice.RawRect> rectangles,int width,int height)
 {
  long area=0;foreach(var r in rectangles){long w=Math.Max(0L,Math.Min((long)width,r.Right)-Math.Max(0L,r.Left)),h=Math.Max(0L,Math.Min((long)height,r.Bottom)-Math.Max(0L,r.Top));area+=w*h;}return area;
 }
}
