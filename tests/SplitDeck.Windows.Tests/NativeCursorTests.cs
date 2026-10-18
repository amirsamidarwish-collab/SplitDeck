using System.Runtime.InteropServices;
using SplitDeck.Core;
using SplitDeck.Windows;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

internal static class NativeCursorTests
{
 internal static Task RasterShapes()
 {
  foreach(var cursor in new[]{System.Windows.Forms.Cursors.Arrow,System.Windows.Forms.Cursors.IBeam,System.Windows.Forms.Cursors.Hand,System.Windows.Forms.Cursors.WaitCursor}){
   var r=NativeCursorRaster.Read(cursor.Handle);
   Check(r.Width>0&&r.Height>0&&r.Black.Length==r.Width*r.Height*4&&r.White.Length==r.Black.Length,"Native cursor dimensions and image lengths must agree.");
   Check(r.HotX>=0&&r.HotX<r.Width&&r.HotY>=0&&r.HotY<r.Height,"Native hotspot must be preserved.");
   Check(!r.Black.SequenceEqual(r.White),"Native raster must preserve transparent/inverting parts, not flatten every cursor.");
  }
  return Task.CompletedTask;
 }
 internal static Task Composition()
 {
  D3D11.D3D11CreateDevice(null,DriverType.Warp,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out ID3D11Device device,out ID3D11DeviceContext context).CheckError();
  using(device)using(context)using(var renderer=new NativeCursorRenderer(device,context)){
   const string vsSource="struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};V VS(uint id:SV_VertexID){V v;v.uv=float2((id<<1)&2,id&2);v.pos=float4(v.uv.x*2-1,1-v.uv.y*2,0,1);return v;}";
   using var vs=device.CreateVertexShader(Compiler.Compile(vsSource,"VS","CursorRegression","vs_4_0").Span);
   using var sampler=device.CreateSamplerState(new SamplerDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaxLOD=float.MaxValue});
   var desc=new Texture2DDescription{Width=128,Height=128,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource};
   using var desktop=device.CreateTexture2D(desc);using var desktopTarget=device.CreateRenderTargetView(desktop);using var desktopView=device.CreateShaderResourceView(desktop);
   using var image=device.CreateTexture2D(desc);using var target=device.CreateRenderTargetView(image);
   desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CPUAccessFlags=CpuAccessFlags.Read;using var staging=device.CreateTexture2D(desc);
   context.ClearRenderTargetView(desktopTarget,new Color4(.2f,.4f,.6f,1));context.OMSetRenderTargets(target);context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);context.VSSetShader(vs);context.PSSetSampler(0,sampler);
   var source=new MonitorInfo("offscreen","offscreen",0,0,128,128,60,null,false,true);
   var surface=new PresentationSurface(source,new VirtualScreenLayout(0,"offscreen",new PixelRect(0,0,128,128),128,128,60));
   using var mono=CreateMonochrome();using var alpha=CreateAlpha();
   foreach(var (cursor,wantsAlpha) in new[]{(mono.Handle,false),(alpha.Handle,true)}){
    var raster=NativeCursorRaster.Read(cursor);Check(raster.HasAlpha==wantsAlpha,"Cursor test must exercise both alpha and AND/XOR native paths.");
    Check(raster.HotX==1&&raster.HotY==1,"Constructed native hotspot must survive GetIconInfo.");
    context.ClearRenderTargetView(target,new Color4(0,0,0,1));
    renderer.Draw([surface],[desktopView],new Native.CursorInfo{Flags=1,Handle=cursor,Position=new(64,64)});
    context.CopyResource(staging,image);var mapped=context.Map(staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
    try{for(int y=0;y<raster.Height;y++)for(int x=0;x<raster.Width;x++)for(int c=0;c<3;c++){
     int offset=(y*raster.Width+x)*4+c;int black=raster.Black[offset],white=raster.White[offset];int desktopByte=new[]{153,102,51}[c];
     double expected=wantsAlpha?black+desktopByte*(white-black)/255.0:(desktopByte&(white^black))^black;
     byte actual=Marshal.ReadByte(mapped.DataPointer+(y+63)*(int)mapped.RowPitch+(x+63)*4+c);
     Check(Math.Abs(actual-expected)<=2,$"Native cursor GPU pixel differs at {x},{y}, channel {c}: expected {expected}, actual {actual}.");
    }}finally{context.Unmap(staging,0);}
   }
  }
  return Task.CompletedTask;
 }
 private static CursorHandle CreateMonochrome()
 {
  nint mask=CreateBitmap(2,4,1,1,[0x40,0,0x80,0,0,0,0xc0,0]);if(mask==0)throw new System.ComponentModel.Win32Exception();
  try{var info=new Native.IconInfo{Mask=mask,HotX=1,HotY=1};nint cursor=CreateIconIndirect(ref info);if(cursor==0)throw new System.ComponentModel.Win32Exception();return new(cursor);}
  finally{Native.DeleteObject(mask);}
 }
 private static CursorHandle CreateAlpha()
 {
  var header=new Native.BitmapInfoHeader{Size=40,Width=2,Height=-2,Planes=1,BitCount=32};nint color=CreateDIBSection(0,ref header,0,out var bits,0,0);if(color==0)throw new System.ComponentModel.Win32Exception();
  nint mask=0;
  try{Marshal.Copy(new byte[]{0,0,128,128,0,255,0,255,0,0,0,0,255,0,0,255},0,bits,16);mask=CreateBitmap(2,2,1,1,[0,0,0,0]);if(mask==0)throw new System.ComponentModel.Win32Exception();
   var info=new Native.IconInfo{Mask=mask,Color=color,HotX=1,HotY=1};nint cursor=CreateIconIndirect(ref info);if(cursor==0)throw new System.ComponentModel.Win32Exception();return new(cursor);
  }finally{Native.DeleteObject(color);if(mask!=0)Native.DeleteObject(mask);}
 }
 private sealed class CursorHandle(nint handle):IDisposable{internal nint Handle=>handle;public void Dispose()=>DestroyIcon(handle);}
 [DllImport("user32.dll")]private static extern nint CreateIconIndirect(ref Native.IconInfo info);
 [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
 [DllImport("gdi32.dll")]private static extern nint CreateBitmap(int width,int height,uint planes,uint bitsPixel,byte[] bits);
 [DllImport("gdi32.dll")]private static extern nint CreateDIBSection(nint dc,ref Native.BitmapInfoHeader info,uint usage,out nint bits,nint section,uint offset);
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}


