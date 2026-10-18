using SplitDeck.Windows;

internal static class GpuDiagnosticTests
{
 internal static Task PendingQueries()
 {
  Check(GpuDiagnosticPolicy.QueryState(0)==GpuQueryReadState.Ready,"Only S_OK is completed query data.");
  Check(GpuDiagnosticPolicy.QueryState(1)==GpuQueryReadState.Pending&&GpuDiagnosticPolicy.QueryState(2)==GpuQueryReadState.Pending,"S_FALSE and other nonzero success results must remain pending without reading data.");
  Check(GpuDiagnosticPolicy.QueryState(unchecked((int)0x887A0005))==GpuQueryReadState.Unavailable,"Failed queries must disable optional profiling rather than report a time.");return Task.CompletedTask;
 }
 internal static Task TimingValidation()
 {
  Check(GpuDiagnosticPolicy.TryMilliseconds(10,30,1000,false,out var ms)&&ms==20,"GPU timestamp duration must use its disjoint-query frequency.");
  Check(!GpuDiagnosticPolicy.TryMilliseconds(10,30,0,false,out _)&&!GpuDiagnosticPolicy.TryMilliseconds(10,30,1000,true,out _)&&!GpuDiagnosticPolicy.TryMilliseconds(30,10,1000,false,out _),"Zero frequencies, disjoint clocks and reversed timestamps must be discarded.");return Task.CompletedTask;
 }
 internal static Task SamplingBound()
 {
  int samples=0;for(int frame=1;frame<=160;frame++)if(GpuDiagnosticPolicy.ShouldSample(frame))samples++;
  Check(samples==10&&!GpuDiagnosticPolicy.ShouldSample(0)&&!GpuDiagnosticPolicy.ShouldSample(-16),"Profiling must sample one in sixteen completed source/render frames, never startup zero.");return Task.CompletedTask;
 }
 internal static Task DirtyArea()
 {
  Vortice.RawRect[] rectangles=[new(-4,-4,4,4),new(8,8,12,12),new(8,8,7,7)];
  Check(GpuDiagnosticPolicy.PixelArea(rectangles,10,10)==20,"Dirty coverage must clip to the source and ignore reversed rectangles.");
  Check(GpuDiagnosticPolicy.PixelArea([new(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue)],1920,1080)==1920L*1080,"Untrusted metadata coordinates must not overflow area calculations.");return Task.CompletedTask;
 }
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
