using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.DXGI;
using System.Diagnostics;

namespace SplitDeck.Windows;

internal sealed class GpuDesktopCapture:IDisposable
{
 private readonly ID3D11Device device;private readonly ID3D11DeviceContext context;private IDXGIOutputDuplication? duplication;private readonly CaptureCadence cadence;private readonly string outputName;private readonly DuplicationRecovery recovery=new(Stopwatch.Frequency);
 private ID3D11Texture2D? image;private bool frameHeld,hasImageForDuplication,identityRotation;
 private readonly Vortice.RawRect[] copyDestinations=new Vortice.RawRect[FrameCopyPolicy.MaximumRectangles];
 private readonly CaptureMetadataProfiler metadata;private readonly GpuQueryTimer copyTimer;
 private readonly CaptureFlow flow=new(Stopwatch.Frequency);
 internal CaptureMetadataSnapshot GpuProfileSnapshot=>metadata.Snapshot(copyTimer.Snapshot) with{Flow=flow.Snapshot};
 internal ID3D11ShaderResourceView? View{get;private set;}
 internal ID3D11Texture2D? Texture=>image;
 internal long FrameCount{get;private set;}
 internal GpuDesktopCapture(string name,ID3D11Device device,ID3D11DeviceContext context,int rate)
 {
  this.device=device;this.context=context;outputName=name;cadence=new CaptureCadence(Stopwatch.Frequency,rate);metadata=new(name);
  try{duplication=CreateDuplication()??throw new InvalidOperationException($"DXGI cannot find {outputName} for GPU capture.");}
  catch(SharpGen.Runtime.SharpGenException e)when(DuplicationRecovery.IsRecoverable(e.ResultCode.Code)){recovery.Begin(Stopwatch.GetTimestamp());}
  copyTimer=new(device,context);
 }
 private IDXGIOutputDuplication? CreateDuplication()
 {
  using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>();
  for(uint a=0;factory.EnumAdapters1(a,out var adapter).Success;a++)using(adapter){
   for(uint o=0;adapter.EnumOutputs(o,out var output).Success;o++)using(output){
    if(output.Description.DeviceName!=outputName)continue;
    using var output1=output.QueryInterface<IDXGIOutput1>();
    var created=output1.DuplicateOutput(device);identityRotation=created.Description.Rotation==ModeRotation.Identity;return created;
   }
  }
  return null;
 }
 internal bool Update(int? pollingRate=null)
 {
  copyTimer.Poll();long now=Stopwatch.GetTimestamp();recovery.ThrowIfExpired(now);
  if(!cadence.Due(now,pollingRate)){flow.Skip();return false;}
  if(duplication is null){
   if(!recovery.ShouldAttempt(now))return false;
   try{duplication=CreateDuplication();}
   catch(SharpGen.Runtime.SharpGenException e)when(DuplicationRecovery.IsRecoverable(e.ResultCode.Code)){return false;}
   if(duplication is null)return false;
  }
  if(frameHeld){var released=duplication.ReleaseFrame();frameHeld=false;if(DuplicationRecovery.IsRecoverable(released.Code)){Invalidate(now);return false;}released.CheckError();}
  flow.Attempt();var result=duplication.AcquireNextFrame(0,out var info,out var resource);
  if(DuplicationRecovery.IsRecoverable(result.Code)){resource?.Dispose();Invalidate(now);return false;}
  if(result.Code==unchecked((int)0x887A0027)){flow.Timeout();return false;}result.CheckError();
  frameHeld=true;
  using(resource){
   if(!CpuCaptureBudget.NeedsImage(info.LastPresentTime,hasImageForDuplication)){flow.Pointer();return false;}
   flow.Image(Stopwatch.GetTimestamp(),info.LastPresentTime);
   using var texture=resource.QueryInterface<ID3D11Texture2D>();var d=texture.Description;
   if(d.Format!=Format.B8G8R8A8_UNorm)throw new InvalidOperationException("GPU capture requires an SDR BGRA display. Turn off HDR for this virtual screen.");
   if(image is null||image.Description.Width!=d.Width||image.Description.Height!=d.Height){
    hasImageForDuplication=false;
    View?.Dispose();image?.Dispose();d.BindFlags=BindFlags.ShaderResource;d.Usage=ResourceUsage.Default;d.CPUAccessFlags=CpuAccessFlags.None;d.MiscFlags=ResourceOptionFlags.None;
    image=device.CreateTexture2D(d);View=device.CreateShaderResourceView(image);
   }
   var plan=metadata.PlanCopy(duplication,info,(int)d.Width,(int)d.Height,hasImageForDuplication,identityRotation,copyDestinations);
   bool sample=copyTimer.Begin(FrameCount+1);try{GpuFrameCopy.Execute(context,image,texture,plan,copyDestinations);}finally{if(sample)copyTimer.End();}
   FrameCount++;metadata.Observe(info,(int)d.Width,(int)d.Height,FrameCount,plan);hasImageForDuplication=true;recovery.FreshFrame();return true;
  }
 }
 private void Invalidate(long now){frameHeld=false;hasImageForDuplication=false;flow.ResetInterval();duplication?.Dispose();duplication=null;recovery.Begin(now);}
 public void Dispose(){copyTimer.Dispose();if(frameHeld)duplication?.ReleaseFrame();View?.Dispose();image?.Dispose();duplication?.Dispose();}
}

internal sealed class CaptureCadence(long ticksPerSecond,int rate)
{
 private readonly int requestedRate=Math.Clamp(rate,1,1000);
 private readonly long period=Math.Max(1,ticksPerSecond/Math.Clamp(rate,1,1000));private long? next;
 internal bool Due(long now,int? pollingRate=null)
 {
  // The presenter already paces these calls. A second equal/faster source clock
  // can discard every other opportunity when cursor/vsync work shifts phase.
  if(pollingRate is >0&&requestedRate>=pollingRate)return true;
  if(next is not long due){next=now+period;return true;}if(now<due)return false;next=due+((now-due)/period+1)*period;return true;
 }
}

internal static class CpuCaptureBudget
{
 internal const long BytesPerSecond=256L*1024*1024;
 internal static int[] Rates(IReadOnlyList<(int Width,int Height,int RequestedHz)> sources)
 {
  if(sources.Any(x=>x.Width<=0||x.Height<=0||x.RequestedHz<=0))throw new ArgumentException("Invalid capture dimensions or rate.");
  double wanted=sources.Sum(x=>(double)x.Width*x.Height*4*Math.Min(30,x.RequestedHz));
  double scale=wanted>BytesPerSecond?BytesPerSecond/wanted:1;
  var rates=sources.Select(x=>Math.Max(1,(int)Math.Floor(Math.Min(30,x.RequestedHz)*scale))).ToArray();
  if(sources.Select((x,i)=>(long)x.Width*x.Height*4*rates[i]).Sum()>BytesPerSecond)throw new InvalidOperationException("These modes exceed the CPU fallback capture budget, even at one frame per second.");
  return rates;
 }
 internal static bool NeedsImage(long lastPresent,bool alreadyCopied)=>lastPresent!=0||!alreadyCopied;
}

internal static class CaptureDevice
{
 internal static (ID3D11Device Device,ID3D11DeviceContext Context) ForOutput(string name)
 {
  using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>();
  for(uint a=0;factory.EnumAdapters1(a,out var adapter).Success;a++)using(adapter){
   for(uint o=0;adapter.EnumOutputs(o,out var output).Success;o++)using(output){
    if(output.Description.DeviceName!=name)continue;
    D3D11.D3D11CreateDevice(adapter,DriverType.Unknown,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out ID3D11Device device,out ID3D11DeviceContext context).CheckError();return(device,context);
   }
  }
  throw new InvalidOperationException($"The physical output {name} is unavailable.");
 }
}
