using SplitDeck.Windows;
using Vortice;
using Vortice.DXGI;

internal static class FrameCopyTests
{
 internal static Task PartialPlan()
 {
  RawRect[] output=new RawRect[64];RawRect[] dirty=[new(1,2,5,6)];OutduplMoveRect[] moves=[new(){SourcePoint=new(80,80),DestinationRect=new(10,12,14,16)}];
  var p=FrameCopyPolicy.Plan(100,100,true,true,true,dirty,moves,output);
  Check(!p.FullCopy&&p.RectangleCount==2&&p.CopyBytes==128&&p.Reason==FrameCopyReason.Partial,"Small dirty and moved destinations must copy their exact byte footprint.");
  Check(output[0].Left==1&&output[1].Left==10&&output[1].Top==12,"Moved pixels must be read at their destination from the CURRENT acquired frame; source coordinates are not replayed from cache.");return Task.CompletedTask;
 }
 internal static Task Fallbacks()
 {
  RawRect[] output=new RawRect[64];RawRect[] small=[new(0,0,1,1)];
  foreach(var test in new[]{(Initialized:false,Rotation:true,Metadata:true,Reason:FrameCopyReason.InitialImage),(Initialized:true,Rotation:false,Metadata:true,Reason:FrameCopyReason.Rotation),(Initialized:true,Rotation:true,Metadata:false,Reason:FrameCopyReason.UnavailableMetadata)}){
   var p=FrameCopyPolicy.Plan(100,100,test.Initialized,test.Rotation,test.Metadata,small,[],output);Check(p.FullCopy&&p.CopyBytes==40000&&p.Reason==test.Reason,"First/resize/recovery, rotation and missing metadata require a full image.");
  }
  Check(FrameCopyPolicy.Plan(100,100,true,true,true,[],[],output).Reason==FrameCopyReason.EmptyMetadata,"Empty update lists cannot prove that a fresh desktop image is unchanged.");
  Check(FrameCopyPolicy.Plan(100,100,true,true,true,[new(-1,0,1,1)],[],output).Reason==FrameCopyReason.InvalidRectangle,"Out-of-bounds metadata must fall back instead of leaving stale pixels.");
  Check(FrameCopyPolicy.Plan(100,100,true,true,true,[new(5,5,4,4)],[],output).Reason==FrameCopyReason.InvalidRectangle,"Reversed metadata must fall back.");return Task.CompletedTask;
 }
 internal static Task Bounds()
 {
  RawRect[] output=new RawRect[64];var tiny=Enumerable.Repeat(new RawRect(0,0,1,1),65).ToArray();
  Check(FrameCopyPolicy.Plan(100,100,true,true,true,tiny,[],output).Reason==FrameCopyReason.TooManyRectangles,"Per-frame copy calls must be bounded.");
  Check(FrameCopyPolicy.Plan(100,100,true,true,true,[new(0,0,100,61)],[],output).Reason==FrameCopyReason.HighCoverage,"Large updates must retain the single full-copy path.");
  Check(!FrameCopyPolicy.Plan(100,100,true,true,true,[new(0,0,100,60)],[],output).FullCopy,"At most 60 percent copied coverage is allowed.");
  Check(FrameCopyPolicy.Plan(100,100,true,true,true,[new(0,0,1,1)],[],Span<RawRect>.Empty).FullCopy,"Insufficient output capacity must never cause an out-of-bounds write.");return Task.CompletedTask;
 }
 internal static Task MetadataBuffers()
 {
  var ready=FrameMetadataBuffer.Validate(0,0,32,24,4,4);
  Check(ready.Available&&!ready.Truncated&&ready.DirtyLength==2&&ready.MoveLength==1,"Complete metadata uses exact native structure counts.");
  foreach(var result in new[]{FrameMetadataBuffer.Validate(-1,0,16,24,4,4),FrameMetadataBuffer.Validate(0,1,16,24,4,4),FrameMetadataBuffer.Validate(0,0,17,24,4,4),FrameMetadataBuffer.Validate(0,0,16,25,4,4)})Check(!result.Available,"Failed, pending or misaligned native metadata cannot authorize a partial image update.");
  var truncated=FrameMetadataBuffer.Validate(unchecked((int)0x887A0003),0,80,24,4,4);
  Check(!truncated.Available&&truncated.Truncated,"An oversized required buffer must trigger full-copy fallback rather than using a prefix.");
  var empty=FrameMetadataBuffer.Validate(0,0,0,0,4,4);Check(empty.Available&&empty.DirtyLength==0&&empty.MoveLength==0,"Valid empty buffers remain distinguishable from unavailable metadata for full-copy fallback.");return Task.CompletedTask;
 }
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
