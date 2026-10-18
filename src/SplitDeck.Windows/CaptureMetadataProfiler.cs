using System.Runtime.InteropServices;
using Vortice;
using Vortice.DXGI;

namespace SplitDeck.Windows;

internal sealed record CaptureCopyReasonSnapshot(string Reason,long Frames);
internal sealed record CaptureCopyPlanSnapshot(long ActualCopyBytes,long PartialFrames,long FullFrames,IReadOnlyList<CaptureCopyReasonSnapshot> Reasons);
internal sealed record CaptureMetadataSnapshot(string Source,int Width,int Height,long CopiedFrames,long FullCopyBytes,long MetadataBytes,long AccumulatedFrames,long CoalescedFrames,long MetadataSamples,long MetadataUnavailable,long MetadataTruncated,long DirtyRectangles,long DirtyPixelArea,long MoveRectangles,long MovePixelArea,GpuTimerSnapshot CopyTiming,CaptureCopyPlanSnapshot CopyPlan,CaptureFlowSnapshot? Flow=null);
internal sealed class CaptureMetadataProfiler(string source)
{
 private readonly RawRect[] dirty=new RawRect[256];private readonly OutduplMoveRect[] moves=new OutduplMoveRect[256];
 private readonly long[] reasonFrames=new long[Enum.GetValues<FrameCopyReason>().Length];
 private FrameMetadataRead lastRead;
 private int width,height;private long frames,copyBytes,actualCopyBytes,partialFrames,fullFrames,metadataBytes,accumulated,coalesced,sampled,unavailable,truncated,dirtyCount,dirtyArea,moveCount,moveArea;
 internal CaptureMetadataSnapshot Snapshot(GpuTimerSnapshot copyTiming)
 {
  var reasons=Enum.GetValues<FrameCopyReason>().Select(reason=>new CaptureCopyReasonSnapshot(reason.ToString(),Interlocked.Read(ref reasonFrames[(int)reason]))).ToArray();
  var plan=new CaptureCopyPlanSnapshot(Interlocked.Read(ref actualCopyBytes),Interlocked.Read(ref partialFrames),Interlocked.Read(ref fullFrames),Array.AsReadOnly(reasons));
  return new(source,Volatile.Read(ref width),Volatile.Read(ref height),Interlocked.Read(ref frames),Interlocked.Read(ref copyBytes),Interlocked.Read(ref metadataBytes),Interlocked.Read(ref accumulated),Interlocked.Read(ref coalesced),Interlocked.Read(ref sampled),Interlocked.Read(ref unavailable),Interlocked.Read(ref truncated),Interlocked.Read(ref dirtyCount),Interlocked.Read(ref dirtyArea),Interlocked.Read(ref moveCount),Interlocked.Read(ref moveArea),copyTiming,plan);
 }
 internal FrameCopyPlan PlanCopy(IDXGIOutputDuplication duplication,OutduplFrameInfo info,int frameWidth,int frameHeight,bool initialized,bool identityRotation,Span<RawRect> destinations)
 {
  lastRead=default;
  if(info.TotalMetadataBufferSize!=0){
   try{
    uint dirtyCapacity=(uint)(dirty.Length*Marshal.SizeOf<RawRect>()),moveCapacity=(uint)(moves.Length*Marshal.SizeOf<OutduplMoveRect>());
    var dirtyResult=duplication.GetFrameDirtyRects(dirtyCapacity,dirty,out uint dirtyBytes);
    var moveResult=duplication.GetFrameMoveRects(moveCapacity,moves,out uint moveBytes);
    lastRead=FrameMetadataBuffer.Validate(dirtyResult.Code,moveResult.Code,dirtyBytes,moveBytes,dirty.Length,moves.Length);
   }catch(Exception){lastRead=default;} // Failure preserves the authoritative full acquired image.
  }
  return FrameCopyPolicy.Plan(frameWidth,frameHeight,initialized,identityRotation,lastRead.Available,dirty.AsSpan(0,lastRead.DirtyLength),moves.AsSpan(0,lastRead.MoveLength),destinations);
 }
 internal void Observe(OutduplFrameInfo info,int frameWidth,int frameHeight,long frame,FrameCopyPlan plan)
 {
  Volatile.Write(ref width,frameWidth);Volatile.Write(ref height,frameHeight);Interlocked.Increment(ref frames);
  // FullCopyBytes retains the old full-image baseline for before/after comparison.
  Interlocked.Add(ref copyBytes,(long)frameWidth*frameHeight*4);Interlocked.Add(ref actualCopyBytes,plan.CopyBytes);
  if(plan.FullCopy)Interlocked.Increment(ref fullFrames);else Interlocked.Increment(ref partialFrames);
  Interlocked.Increment(ref reasonFrames[(int)plan.Reason]);
  Interlocked.Add(ref metadataBytes,info.TotalMetadataBufferSize);Interlocked.Add(ref accumulated,info.AccumulatedFrames);if((bool)info.RectsCoalesced)Interlocked.Increment(ref coalesced);
  if(!GpuDiagnosticPolicy.ShouldSample(frame))return;
  Interlocked.Increment(ref sampled);
  if(lastRead.Truncated){Interlocked.Increment(ref truncated);return;}
  if(!lastRead.Available){Interlocked.Increment(ref unavailable);return;}
  Interlocked.Add(ref dirtyCount,lastRead.DirtyLength);Interlocked.Add(ref dirtyArea,GpuDiagnosticPolicy.PixelArea(dirty.AsSpan(0,lastRead.DirtyLength),frameWidth,frameHeight));
  long movedPixels=0;for(int i=0;i<lastRead.MoveLength;i++){var rectangle=moves[i].DestinationRect;movedPixels+=GpuDiagnosticPolicy.PixelArea(new ReadOnlySpan<RawRect>(in rectangle),frameWidth,frameHeight);}
  Interlocked.Add(ref moveCount,lastRead.MoveLength);Interlocked.Add(ref moveArea,movedPixels);
 }
}
