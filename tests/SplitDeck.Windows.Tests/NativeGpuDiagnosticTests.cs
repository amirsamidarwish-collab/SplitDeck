using System.Diagnostics;
using SplitDeck.Windows;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

internal static class NativeGpuDiagnosticTests
{
 internal static Task TimestampSmoke()
 {
  D3D11.D3D11CreateDevice(null,DriverType.Hardware,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out ID3D11Device device,out ID3D11DeviceContext context).CheckError();
  using(device)using(context)using(var timer=new GpuQueryTimer(device,context)){
   if(!timer.Snapshot.Available)throw new Exception("Native timestamp queries unavailable: "+timer.Snapshot.UnavailableReason);
   var description=new Texture2DDescription{Width=64,Height=64,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Default};
   using var source=device.CreateTexture2D(description);using var destination=device.CreateTexture2D(description);
   var calls=Stopwatch.StartNew();
   for(int frame=1;frame<=80;frame++){
    timer.Poll();bool sampled=timer.Begin(frame);context.CopyResource(destination,source);if(sampled)timer.End();
   }
   calls.Stop();
   // This OFFSCREEN UNIT TEST has no Present to submit the command stream. The
   // test submits once; production GpuQueryTimer never Flushes, maps or waits.
   context.Flush();var deadline=Stopwatch.StartNew();
   while(timer.Snapshot.Pending>0&&deadline.Elapsed<TimeSpan.FromSeconds(3)){timer.Poll();if(timer.Snapshot.Pending>0)Thread.Sleep(1);}
   var result=timer.Snapshot;
   Console.WriteLine($"Offscreen GPU query timer: calls={calls.Elapsed.TotalMilliseconds:F2}ms samples={result.Samples} pending={result.Pending} skipped={result.Skipped} invalid={result.InvalidSamples} average={result.AverageMilliseconds:F6}ms maximum={result.MaxMilliseconds:F6}ms");
   if(!result.Available||result.Samples<1||result.Pending!=0||!double.IsFinite(result.AverageMilliseconds)||result.AverageMilliseconds<0)throw new Exception("Native timestamp query data did not become available without profiler-side waiting or flushing.");
   if(calls.Elapsed>TimeSpan.FromSeconds(3))throw new Exception("Optional query polling consumed an unbounded time in the small offscreen fixture.");
  }
  return Task.CompletedTask;
 }
}
