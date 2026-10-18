using System.Diagnostics;
using System.Runtime.InteropServices;
using SplitDeck.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;
using Forms=System.Windows.Forms;

namespace SplitDeck.Windows;

internal sealed record PresentationSurface(MonitorInfo Source,VirtualScreenLayout Layout);
internal sealed record CursorDiagnostic(int Size,int Error,uint Flags,long Handle,int X,int Y,long Draws,string Status,int ClipLeft,int ClipTop,int ClipRight,int ClipBottom);
internal sealed record PresentationTimingSnapshot(IReadOnlyList<double> GpuUpdateMaxMilliseconds,double DrawMaxMilliseconds,double CursorMaxMilliseconds,double PresentMaxMilliseconds,double PresentIntervalMaxMilliseconds,long PresentIntervalsOver100Ms);
internal sealed record GpuProfileSnapshot(IReadOnlyList<CaptureMetadataSnapshot> Sources,GpuTimerSnapshot? DrawTiming,string Scope);
internal sealed class PresentationHost:IDisposable
{
 private readonly CancellationTokenSource stop=new();private readonly Thread thread;private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);private readonly Action? onStop;private readonly bool enableEmergencyRestore;
 private Exception? failure;internal Exception? Failure=>failure;internal long FramesPresented;internal Task Ready=>ready.Task;
 internal long CaptureTicks,DrawTicks,PresentTicks,PollsCompleted,IdleSkipped;
 internal bool AdaptivePresentation=>inspectFrame is null;
 internal double OutputRefreshHz{get;}
 internal bool ImmediatePresentation{get;private set;}
 private GpuDesktopCapture[] profileSources=[];private GpuQueryTimer? drawTimer;
 internal GpuProfileSnapshot GpuProfileSnapshot=>new(Array.AsReadOnly(Volatile.Read(ref profileSources).Select(x=>x.GpuProfileSnapshot).ToArray()),drawTimer?.Snapshot,"Sampled one in sixteen frames; GPU timestamp intervals on the renderer context, not whole-GPU utilization. Draw intervals can include CPU submission gaps. Metadata rectangle areas cover sampled frames only.");
 internal long CursorBoundaryRepairs,CursorPositionRepairs;
 internal CursorDiagnostic? CursorDiagnostic{get;private set;}
 private long[] gpuUpdateMaxTicks=[];private long drawMaxTicks,cursorMaxTicks,presentMaxTicks,presentIntervalMaxTicks,presentIntervalsOver100Ms;
 internal PresentationTimingSnapshot TimingSnapshot{
  get{var ticks=gpuUpdateMaxTicks;var maxima=new double[ticks.Length];for(int i=0;i<ticks.Length;i++)maxima[i]=Interlocked.Read(ref ticks[i])*1000.0/Stopwatch.Frequency;
   return new(Array.AsReadOnly(maxima),Milliseconds(ref drawMaxTicks),Milliseconds(ref cursorMaxTicks),Milliseconds(ref presentMaxTicks),Milliseconds(ref presentIntervalMaxTicks),Interlocked.Read(ref presentIntervalsOver100Ms));}
 }
 private static double Milliseconds(ref long ticks)=>Interlocked.Read(ref ticks)*1000.0/Stopwatch.Frequency;
 private static void RecordMaximum(ref long target,long value){long previous=Interlocked.Read(ref target);while(value>previous){long observed=Interlocked.CompareExchange(ref target,value,previous);if(observed==previous)return;previous=observed;}}
 internal long[] GpuCapturedFrames=>gpuCaptures.Select(x=>x?.FrameCount??0).ToArray();
 private readonly List<DesktopCapture?> captures=[];private readonly List<GpuDesktopCapture?> gpuCaptures=[];private readonly Native.WndProc procedure;private readonly bool preferGpuCapture;
 internal int GpuCaptureStreams=>gpuCaptures.Count(x=>x is not null);internal int CpuFallbackCaptureStreams=>captures.Count(x=>x is not null);
 internal long CpuReadbackBytes=>captures.Where(x=>x is not null).Sum(x=>Interlocked.Read(ref x!.ReadbackBytes));
 internal string CaptureSummary{get;private set;}="";internal string CaptureDetails{get;private set;}="";
 private readonly Action<ID3D11Device,ID3D11DeviceContext,ID3D11Texture2D,long>? inspectFrame;
 internal PresentationHost(MonitorInfo physical,IReadOnlyList<PresentationSurface> surfaces,Action? restore,bool enableEmergencyRestore=true,Action<ID3D11Device,ID3D11DeviceContext,ID3D11Texture2D,long>? inspectFrame=null,bool preferGpuCapture=true){procedure=WndProc;this.inspectFrame=inspectFrame;this.preferGpuCapture=preferGpuCapture;OutputRefreshHz=physical.RefreshHz;onStop=restore;this.enableEmergencyRestore=enableEmergencyRestore;thread=new(()=>Run(physical,surfaces)){IsBackground=true,Name="SplitDeck presentation"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
 private nint WndProc(nint hwnd,uint msg,nuint wp,nint lp){if(msg==0x0312&&wp==1){onStop?.Invoke();stop.Cancel();return 0;}if(msg==0x0010){onStop?.Invoke();stop.Cancel();return 0;}if(msg==0x0021)return 3;return Native.DefWindowProc(hwnd,msg,wp,lp);}
 private const string Shader="""
 Texture2D image:register(t0); SamplerState linearSampler:register(s0);
 struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
 V VS(uint id:SV_VertexID){V v;v.uv=float2((id<<1)&2,id&2);v.pos=float4(v.uv.x*2-1,1-v.uv.y*2,0,1);return v;}
 float4 PS(V v):SV_TARGET{return image.Sample(linearSampler,v.uv);}
 """;
 private unsafe void Run(MonitorInfo physical,IReadOnlyList<PresentationSurface> surfaces)
 {
  nint hwnd=0;var textures=new List<ID3D11Texture2D?>();var views=new List<ID3D11ShaderResourceView?>();
  try{
   Native.SetProcessDpiAwarenessContext(-4);var wc=new Native.WindowClass{Size=(uint)Marshal.SizeOf<Native.WindowClass>(),Procedure=procedure,Instance=Native.GetModuleHandle(null),ClassName="SplitDeckHost_"+Guid.NewGuid().ToString("N")};
   if(Native.RegisterClassEx(ref wc)==0)throw new System.ComponentModel.Win32Exception();
   hwnd=Native.CreateWindowEx(0x08000088,wc.ClassName,"SplitDeck presentation",0x90000000,physical.X,physical.Y,physical.Width,physical.Height,0,0,wc.Instance,0);
   if(hwnd==0)throw new System.ComponentModel.Win32Exception();
   if(enableEmergencyRestore&&!Native.RegisterHotKey(hwnd,1,0x4007,0x7b))throw new InvalidOperationException("Could not reserve Ctrl+Alt+Shift+F12. Close the app using that shortcut before applying.");
   using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory2>();
   bool requestImmediate=Environment.GetEnvironmentVariable("SPLITDECK_DIAGNOSTIC_IMMEDIATE")=="1";
   bool supportImmediate=false;
   if(requestImmediate)try{using var capabilities=factory.QueryInterface<IDXGIFactory5>();supportImmediate=capabilities.PresentAllowTearing;}catch(SharpGen.Runtime.SharpGenException){}
   var presentation=PresentationPolicy.Select(requestImmediate,supportImmediate);ImmediatePresentation=presentation.SyncInterval==0;
   var created=CaptureDevice.ForOutput(physical.DeviceName);using var device=created.Device;using var context=created.Context;
   var description=new SwapChainDescription1{Width=(uint)physical.Width,Height=(uint)physical.Height,Format=Format.B8G8R8A8_UNorm,BufferCount=2,BufferUsage=Usage.RenderTargetOutput,SampleDescription=new(1,0),SwapEffect=SwapEffect.FlipDiscard,Scaling=Scaling.Stretch,AlphaMode=Vortice.DXGI.AlphaMode.Ignore};
   description.Flags=presentation.CreationFlags;
   using var swap=factory.CreateSwapChainForHwnd(device,hwnd,description);
   using var back=swap.GetBuffer<ID3D11Texture2D>(0);using var target=device.CreateRenderTargetView(back);
   using var vs=device.CreateVertexShader(Compiler.Compile(Shader,"VS","SplitDeck","vs_4_0").Span);using var ps=device.CreatePixelShader(Compiler.Compile(Shader,"PS","SplitDeck","ps_4_0").Span);
   using var sampler=device.CreateSamplerState(new SamplerDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaxLOD=float.MaxValue});
   context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);context.VSSetShader(vs);context.PSSetShader(ps);context.PSSetSampler(0,sampler);context.OMSetRenderTargets(target);
   using var cursorRenderer=new NativeCursorRenderer(device,context);var cursorDesktopViews=new ID3D11ShaderResourceView?[surfaces.Count];
   using var gpuDrawTimer=new GpuQueryTimer(device,context);drawTimer=gpuDrawTimer;
   var unique=surfaces.GroupBy(s=>s.Source.DeviceName).ToArray();var slots=surfaces.Select(s=>Array.FindIndex(unique,g=>g.Key==s.Source.DeviceName)).ToArray();
   var modes=new string[unique.Length];
   foreach(var group in unique){
    GpuDesktopCapture? gpu=null;
    if(preferGpuCapture)try{gpu=new GpuDesktopCapture(group.Key,device,context,group.Max(s=>s.Layout.RefreshHz));}
    catch(Exception e)when(e is SharpGen.Runtime.SharpGenException or InvalidOperationException){modes[gpuCaptures.Count]=$"{group.Key}: GPU unavailable ({e.Message})";}
    gpuCaptures.Add(gpu);captures.Add(null);textures.Add(null);views.Add(null);
   }
   var fallback=Enumerable.Range(0,unique.Length).Where(i=>gpuCaptures[i] is null).ToArray();
   var rates=CpuCaptureBudget.Rates(fallback.Select(i=>(unique[i].First().Source.Width,unique[i].First().Source.Height,unique[i].Max(s=>s.Layout.RefreshHz))).ToArray());
   for(int i=0;i<fallback.Length;i++){
    int slot=fallback[i];var group=unique[slot];var source=group.First().Source;
    captures[slot]=new DesktopCapture(group.Key,rates[i],(long)source.Width*source.Height*4*rates[i]);modes[slot]=$"{modes[slot]} CPU fallback capped at {rates[i]} fps";
   }
   for(int i=0;i<unique.Length;i++)if(gpuCaptures[i] is not null)modes[i]=$"{unique[i].Key}: GPU texture capture";
   Volatile.Write(ref profileSources,gpuCaptures.Where(x=>x is not null).Select(x=>x!).ToArray());
   CaptureDetails=string.Join("; ",modes);
   CaptureSummary=string.Join("; ",unique.Select((group,i)=>string.Join("/",group.Select(x=>x.Layout.Name))+": "+(gpuCaptures[i] is not null?"GPU capture":"CPU fallback up to "+rates[Array.IndexOf(fallback,i)]+" fps")));
   int presentationRate=Math.Clamp(surfaces.Max(s=>s.Layout.RefreshHz),1,(int)Math.Max(1,physical.RefreshHz));
   if(fallback.Length>0)presentationRate=Math.Min(presentationRate,60);
   var presentationCadence=new PresentationCadence(Stopwatch.Frequency,presentationRate);var invalidation=new FrameInvalidationPolicy(Stopwatch.Frequency);var startup=Stopwatch.StartNew();gpuUpdateMaxTicks=new long[gpuCaptures.Count];long previousPresentCompleted=0;
   var cursorInfo=new Native.CursorInfo{Size=Marshal.SizeOf<Native.CursorInfo>()};
   while(!stop.IsCancellationRequested){
    // Even a skipped Present needs explicit pacing; otherwise a static desktop spins.
    long wait=presentationCadence.WaitTicks(Stopwatch.GetTimestamp());if(wait>0&&stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait/(double)Stopwatch.Frequency)))break;
    Forms.Application.DoEvents();
    gpuDrawTimer.Poll();
    long captureStarted=Stopwatch.GetTimestamp();bool contentChanged=false;
    for(int i=0;i<captures.Count;i++){
     if(gpuCaptures[i] is { } gpu){long updateStarted=Stopwatch.GetTimestamp();try{contentChanged|=gpu.Update();}finally{RecordMaximum(ref gpuUpdateMaxTicks[i],Stopwatch.GetTimestamp()-updateStarted);}continue;}
     using var frame=captures[i]!.Take();if(frame is not null){contentChanged=true;if(textures[i] is null||textures[i]!.Description.Width!=frame.Width||textures[i]!.Description.Height!=frame.Height){views[i]?.Dispose();textures[i]?.Dispose();var desc=new Texture2DDescription{Width=(uint)frame.Width,Height=(uint)frame.Height,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource};textures[i]=device.CreateTexture2D(desc);views[i]=device.CreateShaderResourceView(textures[i]);}fixed(byte* p=frame.Pixels)context.UpdateSubresource(textures[i]!,0,null,(nint)p,(uint)(frame.Width*4),0);}
    }
    Interlocked.Add(ref CaptureTicks,Stopwatch.GetTimestamp()-captureStarted);bool allReady=true;
    for(int i=0;i<surfaces.Count;i++){cursorDesktopViews[i]=gpuCaptures[slots[i]]?.View??views[slots[i]];if(cursorDesktopViews[i] is null)allReady=false;}
    long cursorStarted=Stopwatch.GetTimestamp();bool gotCursor=Native.TryGetCursorInfo(ref cursorInfo);int cursorError=gotCursor?0:Marshal.GetLastWin32Error();long cursorQueryTicks=Stopwatch.GetTimestamp()-cursorStarted;RecordMaximum(ref cursorMaxTicks,cursorQueryTicks);
    var cursorState=CursorVisualState.From(gotCursor,cursorInfo);
    bool render=invalidation.NeedsPresent(Stopwatch.GetTimestamp(),contentChanged,cursorState,inspectFrame is not null);
    if(render){
     long drawStarted=Stopwatch.GetTimestamp();bool sampleDraw=gpuDrawTimer.Begin(FramesPresented+1);
     // Flip-model Present detaches back buffer 0 from the output merger.
     context.OMSetRenderTargets(target);context.PSSetShader(ps);context.ClearRenderTargetView(target,new Color4(0.025f,0.035f,0.025f,1));
     for(int i=0;i<surfaces.Count;i++)if(cursorDesktopViews[i] is { } view){var b=surfaces[i].Layout.VisibleBounds;context.RSSetViewport(b.X,b.Y,b.Width,b.Height);context.PSSetShaderResource(0,view);context.Draw(3,0);context.PSSetShaderResource(0,null!);}
     // The OS cursor remains on the actual virtual desktop; this projection is visual only.
     cursorStarted=Stopwatch.GetTimestamp();try{if(gotCursor)cursorRenderer.Draw(surfaces,cursorDesktopViews,cursorInfo);}finally{RecordMaximum(ref cursorMaxTicks,cursorQueryTicks+Stopwatch.GetTimestamp()-cursorStarted);}
     if(sampleDraw)gpuDrawTimer.End();long drawElapsed=Stopwatch.GetTimestamp()-drawStarted;Interlocked.Add(ref DrawTicks,drawElapsed);RecordMaximum(ref drawMaxTicks,drawElapsed);inspectFrame?.Invoke(device,context,back,FramesPresented);
     long presentStarted=Stopwatch.GetTimestamp();try{swap.Present(presentation.SyncInterval,presentation.Flags).CheckError();}finally{long elapsed=Stopwatch.GetTimestamp()-presentStarted;Interlocked.Add(ref PresentTicks,elapsed);RecordMaximum(ref presentMaxTicks,elapsed);}
     long presentCompleted=Stopwatch.GetTimestamp();if(previousPresentCompleted!=0){long interval=presentCompleted-previousPresentCompleted;RecordMaximum(ref presentIntervalMaxTicks,interval);if(interval>Stopwatch.Frequency/10)Interlocked.Increment(ref presentIntervalsOver100Ms);}previousPresentCompleted=presentCompleted;Interlocked.Increment(ref FramesPresented);invalidation.Presented(presentCompleted,cursorState);
     if(allReady)ready.TrySetResult();
    }else Interlocked.Increment(ref IdleSkipped);
    if(PollsCompleted%30==0){Native.GetClipCursor(out var clip);CursorDiagnostic=new(cursorInfo.Size,cursorError,cursorInfo.Flags,(long)cursorInfo.Handle,cursorInfo.Position.X,cursorInfo.Position.Y,Interlocked.Read(ref cursorRenderer.DrawCount),gotCursor?(render?cursorRenderer.LastStatus:"Unchanged; "+cursorRenderer.LastStatus):"GetCursorInfo failed",clip.Left,clip.Top,clip.Right,clip.Bottom);}
    if(!allReady&&startup.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException("Capture did not produce an initial image for every screen.");
    Interlocked.Increment(ref PollsCompleted);

   }
  }catch(Exception e){failure=e;ready.TrySetException(e);onStop?.Invoke();}
  finally{foreach(var c in captures)c?.Dispose();foreach(var c in gpuCaptures)c?.Dispose();foreach(var v in views)v?.Dispose();foreach(var t in textures)t?.Dispose();if(hwnd!=0){if(enableEmergencyRestore)Native.UnregisterHotKey(hwnd,1);Native.DestroyWindow(hwnd);}if(!ready.Task.IsCompleted)ready.TrySetCanceled();}
 }
 public void Dispose(){stop.Cancel();if(Thread.CurrentThread!=thread)thread.Join(TimeSpan.FromSeconds(5));if(!thread.IsAlive)stop.Dispose();}
}
