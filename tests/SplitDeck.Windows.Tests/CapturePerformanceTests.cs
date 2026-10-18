using SplitDeck.Windows;
using Vortice.Direct3D;
using Vortice.Direct3D11;

internal static class CapturePerformanceTests
{
 internal static Task TelemetryJournalIsolation()
 {
  string folder=Path.Combine(Path.GetTempPath(),"SplitDeck-telemetry-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{string journal=Path.Combine(folder,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(journal,"{}");File.WriteAllText(HelperRuntime.PerformancePath(journal),"{}");if(!Directory.GetFiles(folder,"*.json").SequenceEqual(new[]{journal}))throw new Exception("Telemetry must not be discovered as a recovery journal by current or older helpers.");}
  finally{Directory.Delete(folder,true);}return Task.CompletedTask;
 }
 internal static async Task RecoverySettlement()
 {
  await using var controller=new SessionController((_,_)=>System.Diagnostics.Process.GetCurrentProcess(),()=>{},TimeSpan.FromMilliseconds(30));
  try{await controller.SetupDriverAsync();throw new Exception("Expected connection timeout.");}catch(OperationCanceledException){}
  try{await controller.WaitForHelperExitAsync(TimeSpan.FromMilliseconds(30));throw new Exception("Startup cancellation must not imply the helper exited.");}catch(TimeoutException){}
 }
 internal static Task ResourceGuard()
 {
  if(PerformanceBudget.PresentationHealthy(4.3,144)||PerformanceBudget.PresentationHealthy(60,144)||PerformanceBudget.PresentationHealthy(null,144)||!PerformanceBudget.PresentationHealthy(142,144))throw new Exception("A low-resource slideshow must not pass a high-refresh performance trial.");
  if(Math.Abs(PerformanceBudget.CpuPercent(100,1000,700,2000)-40)>0.01)throw new Exception("CPU percent must be normalized across all processors.");
  if(PerformanceBudget.Excess(10,12,1500,20,23) is not null)throw new Exception("Small resource changes should not abort the trial.");
  if(PerformanceBudget.Excess(10,30,1500,20,23) is null||PerformanceBudget.Excess(10,12,200,20,23) is null||PerformanceBudget.Excess(10,12,1500,20,60) is null)throw new Exception("The benchmark must stop excessive CPU/GPU usage or memory pressure.");
  return Task.CompletedTask;
 }
 internal static Task Cadence()
 {
  var cadence=new CaptureCadence(1_000_000,60);int count=0;
  for(int tick=0;tick<144;tick++)if(cadence.Due(tick*1_000_000L/144))count++;
  if(count<59||count>61)throw new Exception($"A 60 Hz source on a 144 Hz presenter should sample about 60 frames, got {count}.");
  return Task.CompletedTask;
 }
 internal static Task Budget()
 {
  var sources=new[]{(3840,2160,144),(2560,1440,144)};
  var rates=CpuCaptureBudget.Rates(sources);
  if(rates.Length!=2||rates.Any(x=>x<1||x>30))throw new Exception("CPU fallback must have bounded rates.");
  long bytes=sources.Select((s,i)=>(long)s.Item1*s.Item2*4*rates[i]).Sum();
  if(bytes>CpuCaptureBudget.BytesPerSecond)throw new Exception("CPU fallback exceeds shared memory bandwidth budget.");
  if(CpuCaptureBudget.NeedsImage(0,true)||!CpuCaptureBudget.NeedsImage(0,false)||!CpuCaptureBudget.NeedsImage(1,true))throw new Exception("Pointer-only events must reuse the previous desktop image.");
  return Task.CompletedTask;
 }
 private sealed class CaptureMarker:System.Windows.Forms.Form{protected override bool ShowWithoutActivation=>true;}
 internal static Task NativeGpu()
 {
  var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var thread=new Thread(()=>{
   try{
    var source=new DisplayService().Detect().First(x=>!x.IsVirtual);Native.SetProcessDpiAwarenessContext(-4);
    using var marker=new CaptureMarker{FormBorderStyle=System.Windows.Forms.FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=System.Windows.Forms.FormStartPosition.Manual,Bounds=new System.Drawing.Rectangle(source.X+80,source.Y+80,64,64),BackColor=System.Drawing.Color.FromArgb(32,192,80)};marker.Show();marker.Update();
    var created=CaptureDevice.ForOutput(source.DeviceName);using var device=created.Device;using var context=created.Context;
    using var capture=new GpuDesktopCapture(source.DeviceName,device,context,144);
    using var pixel=device.CreateTexture2D(new Texture2DDescription{Width=1,Height=1,MipLevels=1,ArraySize=1,Format=Vortice.DXGI.Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Staging,CPUAccessFlags=CpuAccessFlags.Read});
    var deadline=DateTime.UtcNow.AddSeconds(5);bool correct=false;
    while(DateTime.UtcNow<deadline){
     System.Windows.Forms.Application.DoEvents();
     if(capture.Update()&&capture.Texture is { } texture){
      // One-pixel readback belongs only to this regression; production capture never maps.
      context.CopySubresourceRegion(pixel,0,0,0,0,texture,0,new Vortice.Mathematics.Box(100,100,0,101,101,1));
      var mapped=context.Map(pixel,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
      try{correct=Math.Abs(System.Runtime.InteropServices.Marshal.ReadByte(mapped.DataPointer)-80)<8&&Math.Abs(System.Runtime.InteropServices.Marshal.ReadByte(mapped.DataPointer,1)-192)<8&&Math.Abs(System.Runtime.InteropServices.Marshal.ReadByte(mapped.DataPointer,2)-32)<8;}
      finally{context.Unmap(pixel,0);}
      if(correct)break;
     }
     Thread.Sleep(10);
    }
    if(!correct||capture.View is null||capture.FrameCount<1)throw new Exception("GPU capture did not reproduce the visible desktop marker pixel.");completed.TrySetResult();
   }catch(Exception e){completed.TrySetException(e);}
  }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completed.Task;
 }
 internal static async Task NativeFallback()
 {
  var source=new DisplayService().Detect().First(x=>!x.IsVirtual);
  var area=source with{X=source.X+25,Y=source.Y+25,Width=320,Height=180};
  var surface=new PresentationSurface(source,new SplitDeck.Core.VirtualScreenLayout(0,"Fallback",new SplitDeck.Core.PixelRect(0,0,320,180),source.Width,source.Height,144));
  using var host=new PresentationHost(area,[surface],null,false,preferGpuCapture:false);
  await host.Ready.WaitAsync(TimeSpan.FromSeconds(8));
  if(host.GpuCaptureStreams!=0||host.CpuFallbackCaptureStreams!=1||host.CpuReadbackBytes<=0)throw new Exception("The bounded CPU fallback did not produce an initial frame.");
  if(host.Failure is not null)throw host.Failure;
 }
}
