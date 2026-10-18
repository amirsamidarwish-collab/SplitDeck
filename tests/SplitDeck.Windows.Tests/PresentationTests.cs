using SplitDeck.Core;
using SplitDeck.Windows;
using Vortice.Direct3D11;

internal static class PresentationTests
{
 internal static async Task DirectApply()
 {
  var physical=new DisplayService().Detect().First(x=>!x.IsVirtual);
  var area=physical with{X=physical.X+25,Y=physical.Y+25,Width=320,Height=180};
  var surface=new PresentationSurface(area,new VirtualScreenLayout(0,"Direct Apply",new PixelRect(0,0,320,180),320,180,30));
  int stopped=0,confirmationSeen=0;
  using var host=new PresentationHost(area,[surface],()=>Interlocked.Increment(ref stopped),inspectFrame:(_,_,_,_)=>{
   foreach(System.Windows.Forms.Form form in System.Windows.Forms.Application.OpenForms)
    if(form.Text.StartsWith("Keep this layout?",StringComparison.Ordinal))Interlocked.Exchange(ref confirmationSeen,1);
  });
  await host.Ready.WaitAsync(TimeSpan.FromSeconds(20));
  bool shortcutAvailable=Native.RegisterHotKey(0,99,0x4007,0x7b);
  if(shortcutAvailable)Native.UnregisterHotKey(0,99);
  if(shortcutAvailable)throw new Exception("The presenter did not reserve emergency restore.");
  await Task.Delay(TimeSpan.FromSeconds(16));
  if(Volatile.Read(ref stopped)!=0)throw new Exception("Direct Apply reverted without a restore request or rendering failure.");
  if(Volatile.Read(ref confirmationSeen)!=0)throw new Exception("Direct Apply displayed a confirmation popup.");
  if(host.Failure is not null)throw host.Failure;
 }
 internal static async Task Run()
 {
  var source=new DisplayService().Detect().First(x=>!x.IsVirtual);
  var hostArea=source with{X=source.X+25,Y=source.Y+25,Width=320,Height=180};
  var surface=new PresentationSurface(source,new VirtualScreenLayout(0,"Regression",new PixelRect(0,0,320,180),source.Width,source.Height,30));
  var observed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  using var host=new PresentationHost(hostArea,[surface],null,false,inspectFrame:(device,context,back,frame)=>{
   if(frame<3)return;
   var targets=new ID3D11RenderTargetView[1];context.OMGetRenderTargets(1,targets,out var depth);
   try{observed.TrySetResult(targets[0] is not null);}finally{targets[0]?.Dispose();depth?.Dispose();}
  });
  await host.Ready.WaitAsync(TimeSpan.FromSeconds(20));
  if(!await observed.Task.WaitAsync(TimeSpan.FromSeconds(10)))throw new Exception("The back buffer is detached after Present; later desktop frames cannot be drawn.");
  if(host.GpuCaptureStreams!=1||host.CpuFallbackCaptureStreams!=0||host.CpuReadbackBytes!=0)throw new Exception("Compatible physical capture should stay on the GPU without CPU readback.");
  if(host.Failure is not null)throw host.Failure;
 }
}
