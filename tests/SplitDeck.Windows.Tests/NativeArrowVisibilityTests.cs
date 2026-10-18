using System.Runtime.InteropServices;
using SplitDeck.Core;
using SplitDeck.Windows;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

internal static class NativeArrowVisibilityTests
{
 // Offscreen only: use the real Windows arrow and production renderer, without
 // moving the pointer, opening a window, changing a display, or faking native input.
 internal static Task BothScreens()
 {
  nint arrow=System.Windows.Forms.Cursors.Arrow.Handle;var raster=NativeCursorRaster.Read(arrow);
  D3D11.D3D11CreateDevice(null,DriverType.Warp,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out ID3D11Device device,out ID3D11DeviceContext context).CheckError();
  using(device)using(context)using(var renderer=new NativeCursorRenderer(device,context)){
   const string vertex="struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};V VS(uint id:SV_VertexID){V v;v.uv=float2((id<<1)&2,id&2);v.pos=float4(v.uv.x*2-1,1-v.uv.y*2,0,1);return v;}";
   using var vs=device.CreateVertexShader(Compiler.Compile(vertex,"VS","NativeArrowVisibility","vs_4_0").Span);
   using var sampler=device.CreateSamplerState(new SamplerDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaxLOD=float.MaxValue});
   var description=new Texture2DDescription{Width=420,Height=180,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource};
   using var targetImage=device.CreateTexture2D(description);using var target=device.CreateRenderTargetView(targetImage);
   description.BindFlags=BindFlags.None;description.Usage=ResourceUsage.Staging;description.CPUAccessFlags=CpuAccessFlags.Read;using var readback=device.CreateTexture2D(description);
   description.BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource;description.Usage=ResourceUsage.Default;description.CPUAccessFlags=CpuAccessFlags.None;description.Width=640;description.Height=360;
   using var gameImage=device.CreateTexture2D(description);using var gameTarget=device.CreateRenderTargetView(gameImage);using var gameView=device.CreateShaderResourceView(gameImage);
   description.Width=200;using var portraitImage=device.CreateTexture2D(description);using var portraitTarget=device.CreateRenderTargetView(portraitImage);using var portraitView=device.CreateShaderResourceView(portraitImage);
   var background=new Color4(.2f,.4f,.6f,1);context.ClearRenderTargetView(gameTarget,background);context.ClearRenderTargetView(portraitTarget,background);
   context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);context.VSSetShader(vs);context.PSSetSampler(0,sampler);
   var game=new MonitorInfo("game","game",-1000,100,640,360,144,null,true,true);
   var portrait=new MonitorInfo("portrait","portrait",-360,100,200,360,30,null,false,true);
   PresentationSurface[] surfaces=[new(game,new(0,"Game",new(0,0,320,180),640,360,144)),new(portrait,new(1,"Portrait",new(320,0,100,180),200,360,30))];
   ID3D11ShaderResourceView?[] views=[gameView,portraitView];
   var cases=new[]{(Name:"game",X:-680,Y:280,Flags:1u,ExpectedSlot:0),(Name:"portrait",X:-260,Y:280,Flags:1u,ExpectedSlot:1),(Name:"game-again",X:-680,Y:280,Flags:1u,ExpectedSlot:0),(Name:"portrait-right-edge",X:-168,Y:280,Flags:1u,ExpectedSlot:1),(Name:"hidden",X:-260,Y:280,Flags:0u,ExpectedSlot:-1),(Name:"suppressed",X:-260,Y:280,Flags:2u,ExpectedSlot:-1)};
   foreach(var test in cases){
    for(int frame=0;frame<8;frame++){context.OMSetRenderTargets(target);context.ClearRenderTargetView(target,background);renderer.Draw(surfaces,views,new Native.CursorInfo{Size=Marshal.SizeOf<Native.CursorInfo>(),Handle=arrow,Flags=test.Flags,Position=new(test.X,test.Y)});}
    context.CopyResource(readback,targetImage);var mapped=context.Map(readback,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
    try{
     int[] changed=[0,0];
     for(int y=0;y<180;y++)for(int x=0;x<420;x++){
      nint pixel=mapped.DataPointer+y*(int)mapped.RowPitch+x*4;
      bool differs=Math.Abs(Marshal.ReadByte(pixel)-153)>2||Math.Abs(Marshal.ReadByte(pixel,1)-102)>2||Math.Abs(Marshal.ReadByte(pixel,2)-51)>2;
      if(differs)changed[x<320?0:1]++;
     }
     Console.WriteLine($"Native arrow {test.Name}: source144/30, raster{raster.Width}x{raster.Height}, hotspot{raster.HotX},{raster.HotY}, alpha={raster.HasAlpha}, changedpixels={changed[0]}/{changed[1]}");
     if(test.ExpectedSlot<0){if(changed.Sum()!=0)throw new Exception("Hidden or suppressed native cursor was drawn.");}
     else{if(changed[test.ExpectedSlot]<1)throw new Exception($"Real native arrow is invisible in {test.Name}.");if(changed[1-test.ExpectedSlot]!=0)throw new Exception($"Real native arrow escaped its screen in {test.Name}.");}
     string? imagePath=Environment.GetEnvironmentVariable("SPLITDECK_TEST_CURSOR_PNG");
     if(imagePath is not null&&test.Name=="portrait")SaveImage(imagePath,mapped);
    }finally{context.Unmap(readback,0);}
   }
  }
  return Task.CompletedTask;
 }
 private static void SaveImage(string path,MappedSubresource mapped)
 {
  using var image=new System.Drawing.Bitmap(420,180,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  var data=image.LockBits(new System.Drawing.Rectangle(0,0,420,180),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  try{byte[] row=new byte[420*4];for(int y=0;y<180;y++){Marshal.Copy(mapped.DataPointer+y*(int)mapped.RowPitch,row,0,row.Length);Marshal.Copy(row,0,data.Scan0+y*data.Stride,row.Length);}}
  finally{image.UnlockBits(data);}
  image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
 }
}
