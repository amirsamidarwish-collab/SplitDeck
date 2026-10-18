using Vortice;
using Vortice.DXGI;

namespace SplitDeck.Windows;

internal enum FrameCopyReason{Partial,InitialImage,Rotation,UnavailableMetadata,EmptyMetadata,InvalidRectangle,TooManyRectangles,HighCoverage}
internal readonly record struct FrameCopyPlan(bool FullCopy,int RectangleCount,long CopyBytes,FrameCopyReason Reason);
internal readonly record struct FrameMetadataRead(bool Available,bool Truncated,int DirtyLength,int MoveLength);
internal static class FrameMetadataBuffer
{
 internal static FrameMetadataRead Validate(int dirtyResult,int moveResult,uint dirtyBytes,uint moveBytes,int dirtyCapacity,int moveCapacity)
 {
  uint dirtyStride=(uint)System.Runtime.InteropServices.Marshal.SizeOf<RawRect>(),moveStride=(uint)System.Runtime.InteropServices.Marshal.SizeOf<OutduplMoveRect>();
  if(dirtyCapacity<0||moveCapacity<0)return default;
  if(dirtyBytes>(long)dirtyCapacity*dirtyStride||moveBytes>(long)moveCapacity*moveStride)return new(false,true,0,0);
  if(dirtyResult!=0||moveResult!=0||dirtyBytes%dirtyStride!=0||moveBytes%moveStride!=0)return default;
  return new(true,false,(int)(dirtyBytes/dirtyStride),(int)(moveBytes/moveStride));
 }
}
internal static class FrameCopyPolicy
{
 internal const int MaximumRectangles=64;
 internal static FrameCopyPlan Plan(int width,int height,bool initialized,bool identityRotation,bool metadataAvailable,ReadOnlySpan<RawRect> dirty,ReadOnlySpan<OutduplMoveRect> moves,Span<RawRect> destinations)
 {
  if(width<=0||height<=0)throw new ArgumentOutOfRangeException(nameof(width));long fullBytes=checked((long)width*height*4);
  FrameCopyPlan Full(FrameCopyReason reason)=>new(true,0,fullBytes,reason);
  if(!initialized)return Full(FrameCopyReason.InitialImage);if(!identityRotation)return Full(FrameCopyReason.Rotation);if(!metadataAvailable)return Full(FrameCopyReason.UnavailableMetadata);
  int count=dirty.Length+moves.Length;if(count==0)return Full(FrameCopyReason.EmptyMetadata);if(count>MaximumRectangles||count>destinations.Length)return Full(FrameCopyReason.TooManyRectangles);
  long pixels=0,maximumPartialPixels=(long)width*height*3/5;
  for(int i=0;i<count;i++){
   var rectangle=i<dirty.Length?dirty[i]:moves[i-dirty.Length].DestinationRect;
   if(rectangle.Left<0||rectangle.Top<0||rectangle.Right>width||rectangle.Bottom>height||rectangle.Right<=rectangle.Left||rectangle.Bottom<=rectangle.Top)return Full(FrameCopyReason.InvalidRectangle);
   pixels+=(long)(rectangle.Right-rectangle.Left)*(rectangle.Bottom-rectangle.Top);destinations[i]=rectangle;
   if(pixels>maximumPartialPixels)return Full(FrameCopyReason.HighCoverage);
  }
  // Sum includes overlaps deliberately: conservative coverage bounds both copied
  // bytes and command count, while duplicate destinations remain pixel-correct.
  return new(false,count,pixels*4,FrameCopyReason.Partial);
 }
}
internal static class GpuFrameCopy
{
 internal static void Execute(Vortice.Direct3D11.ID3D11DeviceContext context,Vortice.Direct3D11.ID3D11Texture2D destination,Vortice.Direct3D11.ID3D11Texture2D acquired,FrameCopyPlan plan,ReadOnlySpan<RawRect> rectangles)
 {
  if(plan.FullCopy){context.CopyResource(destination,acquired);return;}
  for(int i=0;i<plan.RectangleCount;i++){
   var r=rectangles[i];
   // The acquired image already includes every move and dirty update. Reading
   // its destination pixels also handles overlapping moves without a scratch image.
   context.CopySubresourceRegion(destination,0,(uint)r.Left,(uint)r.Top,0,acquired,0,new Vortice.Mathematics.Box(r.Left,r.Top,0,r.Right,r.Bottom,1));
  }
 }
}
