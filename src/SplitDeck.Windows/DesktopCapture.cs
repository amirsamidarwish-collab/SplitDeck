using System.Buffers;
using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SplitDeck.Windows;

internal sealed class CapturedFrame(byte[] pixels,int width,int height):IDisposable
{
 internal byte[] Pixels {get;}=pixels;internal int Width{get;}=width;internal int Height{get;}=height;
 public void Dispose()=>ArrayPool<byte>.Shared.Return(Pixels);
}
internal sealed class DesktopCapture:IDisposable
{
 private readonly CancellationTokenSource stop=new();private readonly Task worker;private CapturedFrame? latest;private Exception? failure;private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
 internal Task Ready=>ready.Task;internal long FrameCount,ReadbackBytes,NonblockingMisses;internal CapturedFrame? Take(){if(failure is not null)throw new InvalidOperationException("Display capture stopped. "+failure.Message,failure);return Interlocked.Exchange(ref latest,null);}
 internal DesktopCapture(string deviceName,int rate,long byteBudget=CpuCaptureBudget.BytesPerSecond){worker=Task.Factory.StartNew(()=>Run(deviceName,rate,byteBudget),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
 private unsafe void Run(string name,int rate,long byteBudget)
 {
  try{
   using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>();
   IDXGIAdapter1? chosen=null;IDXGIOutput? output=null;
   for(uint a=0;factory.EnumAdapters1(a,out var adapter).Success;a++){
    bool found=false;for(uint o=0;adapter.EnumOutputs(o,out var candidate).Success;o++){if(candidate.Description.DeviceName==name){chosen=adapter;output=candidate;found=true;break;}candidate.Dispose();}if(found)break;adapter.Dispose();
   }
   if(chosen is null||output is null)throw new InvalidOperationException($"DXGI cannot find {name}. The installed display driver may not support desktop capture.");
   using(chosen)using(output){
    D3D11.D3D11CreateDevice(chosen,DriverType.Unknown,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out ID3D11Device device,out ID3D11DeviceContext context).CheckError();
    using(device)using(context)using(var output1=output.QueryInterface<IDXGIOutput1>())using(var duplication=output1.DuplicateOutput(device)){
     var staging=new List<ID3D11Texture2D>();var pending=new Queue<int>();var available=new Queue<int>();int width=0,height=0;bool copied=false;
     var period=TimeSpan.FromSeconds(1.0/Math.Clamp(rate,1,30));var clock=Stopwatch.StartNew();TimeSpan next=TimeSpan.Zero;
     void ReadPending()
     {
      if(!pending.TryPeek(out int slot))return;
      var result=context.Map(staging[slot],0,MapMode.Read,Vortice.Direct3D11.MapFlags.DoNotWait,out var map);
      if(result.Code==unchecked((int)0x887A000A)){Interlocked.Increment(ref NonblockingMisses);return;}result.CheckError();
      byte[]? pixels=null;
      try{
       pixels=ArrayPool<byte>.Shared.Rent(checked(width*height*4));
       fixed(byte* dst=pixels){for(int y=0;y<height;y++)Buffer.MemoryCopy((byte*)map.DataPointer+y*map.RowPitch,dst+y*width*4,width*4,width*4);}
       var frame=new CapturedFrame(pixels,width,height);pixels=null;Interlocked.Exchange(ref latest,frame)?.Dispose();Interlocked.Increment(ref FrameCount);Interlocked.Add(ref ReadbackBytes,(long)width*height*4);ready.TrySetResult();
      }finally{if(pixels is not null)ArrayPool<byte>.Shared.Return(pixels);context.Unmap(staging[slot],0);pending.Dequeue();available.Enqueue(slot);}
     }
     try{while(!stop.IsCancellationRequested){
      if(clock.Elapsed<next){stop.Token.WaitHandle.WaitOne(next-clock.Elapsed);continue;}next=clock.Elapsed+period;
      ReadPending();if(staging.Count>0&&available.Count==0)continue;
      var result=duplication.AcquireNextFrame(0,out var info,out var resource);
      if(result.Code==unchecked((int)0x887A0027))continue;result.CheckError();
      try{using(resource)using(var texture=resource.QueryInterface<ID3D11Texture2D>()){
       if(!CpuCaptureBudget.NeedsImage(info.LastPresentTime,copied))continue;
       var d=texture.Description;
       if(d.Format!=Format.B8G8R8A8_UNorm)throw new InvalidOperationException("Only SDR BGRA capture is supported in this build. Turn off HDR for this virtual screen.");
       long frameBytes=checked((long)d.Width*d.Height*4);
       if(byteBudget<=0||frameBytes>byteBudget)throw new InvalidOperationException("This output exceeds its CPU capture budget. Select a lower render resolution.");
       period=TimeSpan.FromSeconds(Math.Max(1.0/Math.Clamp(rate,1,30),frameBytes/(double)byteBudget));
       if(staging.Count==0||width!=(int)d.Width||height!=(int)d.Height){
        foreach(var old in staging)old.Dispose();staging.Clear();pending.Clear();available.Clear();width=(int)d.Width;height=(int)d.Height;
        d.BindFlags=BindFlags.None;d.Usage=ResourceUsage.Staging;d.CPUAccessFlags=CpuAccessFlags.Read;d.MiscFlags=ResourceOptionFlags.None;
        for(int i=0;i<3;i++){staging.Add(device.CreateTexture2D(d));available.Enqueue(i);}
       }
       int slot=available.Dequeue();context.CopyResource(staging[slot],texture);context.Flush();pending.Enqueue(slot);copied=true;ReadPending();
      }}finally{duplication.ReleaseFrame().CheckError();}
     }}finally{foreach(var texture in staging)texture.Dispose();}
    }
   }
  }catch(Exception e){if(!stop.IsCancellationRequested){failure=e;ready.TrySetException(e);}}
  finally{if(!ready.Task.IsCompleted)ready.TrySetCanceled();}
 }
 public void Dispose(){stop.Cancel();try{worker.Wait(TimeSpan.FromSeconds(3));}catch(AggregateException){}Interlocked.Exchange(ref latest,null)?.Dispose();if(worker.IsCompleted)stop.Dispose();}
}
