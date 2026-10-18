using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using SplitDeck.Core;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;

namespace SplitDeck.Windows;

// Windows rasterizes only the small cursor, once per HCURSOR change. Desktop pixels
// stay on the GPU. Two backgrounds retain both alpha blending and AND/XOR cursors.
internal sealed class NativeCursorRenderer:IDisposable
{
 private const string Shader="""
 Texture2D desktop:register(t0);Texture2D blackImage:register(t1);Texture2D whiteImage:register(t2);
 SamplerState linearSampler:register(s0);SamplerState pointSampler:register(s1);
 cbuffer CursorData:register(b0){float4 sourceBounds;float4 options;};
 float4 PS(float4 position:SV_POSITION,float2 uv:TEXCOORD0):SV_TARGET{
  float3 d=desktop.Sample(linearSampler,sourceBounds.xy+uv*sourceBounds.zw).rgb;
  float3 b=blackImage.Sample(pointSampler,uv).rgb,w=whiteImage.Sample(pointSampler,uv).rgb;
  if(options.x>0)return float4(saturate(b+d*(w-b)),1);
  uint3 bi=(uint3)round(b*255),wi=(uint3)round(w*255),di=(uint3)round(d*255);
  return float4((float3)((di&(wi^bi))^bi)/255,1);
 }
 """;
 private readonly ID3D11Device device;private readonly ID3D11DeviceContext context;
 private readonly ID3D11PixelShader shader;private readonly ID3D11Buffer constants;private readonly ID3D11SamplerState pointSampler;private readonly ID3D11RasterizerState clippedRasterizer;
 private readonly CursorShapeCache<GpuCursorShape> cache=new();
 internal string LastStatus{get;private set;}="Not sampled";
 internal long DrawCount;
 internal NativeCursorRenderer(ID3D11Device device,ID3D11DeviceContext context)
 {
  this.device=device;this.context=context;shader=device.CreatePixelShader(Compiler.Compile(Shader,"PS","SplitDeckNativeCursor","ps_4_0").Span);
  constants=device.CreateBuffer(new BufferDescription{ByteWidth=32,Usage=ResourceUsage.Default,BindFlags=BindFlags.ConstantBuffer});
  pointSampler=device.CreateSamplerState(new SamplerDescription{Filter=Filter.MinMagMipPoint,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaxLOD=float.MaxValue});
  clippedRasterizer=device.CreateRasterizerState(new RasterizerDescription{FillMode=FillMode.Solid,CullMode=CullMode.None,ScissorEnable=true,DepthClipEnable=true});
 }
 internal void Draw(IReadOnlyList<PresentationSurface> surfaces,IReadOnlyList<ID3D11ShaderResourceView?> desktopViews,Native.CursorInfo cursor)
 {
  if(!CursorProjection.Visible(cursor.Flags)||cursor.Handle==0){LastStatus=$"Hidden flags={cursor.Flags} handle={cursor.Handle}";return;}
  int slot=-1;
  for(int i=0;i<surfaces.Count;i++){var s=surfaces[i].Source;if(cursor.Position.X>=s.X&&cursor.Position.Y>=s.Y&&cursor.Position.X<s.X+s.Width&&cursor.Position.Y<s.Y+s.Height){slot=i;break;}}
  if(slot<0||desktopViews[slot] is not { } desktop){LastStatus=$"Outside source or no image at {cursor.Position.X},{cursor.Position.Y}";return;}
  GpuCursorShape shape;
  try{shape=cache.Get(cursor.Handle,CreateShape,x=>x.Dispose());}
  catch(System.ComponentModel.Win32Exception e){LastStatus="Raster failed: "+e.Message;return;} // A cursor can be destroyed between query and copy; retry next frame.
  var surface=surfaces[slot];var bounds=surface.Layout.VisibleBounds;
  var p=CursorProjection.Project(surface.Source,bounds,cursor.Position.X,cursor.Position.Y,shape.Width,shape.Height,shape.HotX,shape.HotY);
  var data=new CursorConstants{Bounds=new(p.SourceU,p.SourceV,p.SourceWidth,p.SourceHeight),Options=new(shape.HasAlpha?1:0,0,0,0)};
  context.UpdateSubresource(in data,constants);context.PSSetConstantBuffer(0,constants);context.PSSetShader(shader);context.PSSetSampler(1,pointSampler);
  context.RSSetState(clippedRasterizer);context.RSSetScissorRect(bounds.X,bounds.Y,bounds.X+bounds.Width,bounds.Y+bounds.Height);
  context.RSSetViewport(p.X,p.Y,p.Width,p.Height);context.PSSetShaderResource(0,desktop);context.PSSetShaderResource(1,shape.BlackView);context.PSSetShaderResource(2,shape.WhiteView);
  context.Draw(3,0);context.PSSetShaderResource(0,null!);context.PSSetShaderResource(1,null!);context.PSSetShaderResource(2,null!);context.RSSetState(null);
  Interlocked.Increment(ref DrawCount);LastStatus=slot switch{0=>"Drawn on source 1",1=>"Drawn on source 2",_=>"Drawn on source 3"};
 }
 private GpuCursorShape CreateShape(nint handle)
 {
  var raster=NativeCursorRaster.Read(handle);
  ID3D11Texture2D? black=null,white=null;ID3D11ShaderResourceView? blackView=null,whiteView=null;
  try{black=Texture(raster.Black,raster.Width,raster.Height);white=Texture(raster.White,raster.Width,raster.Height);blackView=device.CreateShaderResourceView(black);whiteView=device.CreateShaderResourceView(white);return new(raster,black,white,blackView,whiteView);}
  catch{blackView?.Dispose();whiteView?.Dispose();black?.Dispose();white?.Dispose();throw;}
 }
 private unsafe ID3D11Texture2D Texture(byte[] pixels,int width,int height)
 {
  var description=new Texture2DDescription{Width=(uint)width,Height=(uint)height,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource};
  fixed(byte* p=pixels)return device.CreateTexture2D(description,new SubresourceData((nint)p,(uint)(width*4)));
 }
 public void Dispose(){cache.Clear(x=>x.Dispose());clippedRasterizer.Dispose();pointSampler.Dispose();constants.Dispose();shader.Dispose();}
 [StructLayout(LayoutKind.Sequential)]private struct CursorConstants{public Vector4 Bounds,Options;}
 private sealed class GpuCursorShape(NativeCursorRaster raster,ID3D11Texture2D black,ID3D11Texture2D white,ID3D11ShaderResourceView blackView,ID3D11ShaderResourceView whiteView):IDisposable
 {
  internal int Width=>raster.Width;internal int Height=>raster.Height;internal int HotX=>raster.HotX;internal int HotY=>raster.HotY;internal bool HasAlpha=>raster.HasAlpha;
  internal ID3D11ShaderResourceView BlackView=>blackView;internal ID3D11ShaderResourceView WhiteView=>whiteView;
  public void Dispose(){blackView.Dispose();whiteView.Dispose();black.Dispose();white.Dispose();}
 }
}

internal sealed record NativeCursorRaster(int Width,int Height,int HotX,int HotY,bool HasAlpha,byte[] Black,byte[] White)
{
 internal static NativeCursorRaster Read(nint handle)
 {
  if(!Native.GetIconInfo(handle,out var info))throw new System.ComponentModel.Win32Exception();
  try{
   if(Native.GetBitmapObject(info.Color!=0?info.Color:info.Mask,Marshal.SizeOf<Native.BitmapDescription>(),out var bitmap)==0)throw new System.ComponentModel.Win32Exception();
   int width=bitmap.Width,height=info.Color!=0?bitmap.Height:bitmap.Height/2;
   if(width<=0||height<=0||width>512||height>512)throw new System.ComponentModel.Win32Exception("Unsupported cursor dimensions.");
   bool hasAlpha=false;
   if(info.Color!=0&&bitmap.BitsPixel==32){
    nint dc=Native.CreateCompatibleDC(0);if(dc==0)throw new System.ComponentModel.Win32Exception();
    try{var header=new Native.BitmapInfoHeader{Size=(uint)Marshal.SizeOf<Native.BitmapInfoHeader>(),Width=width,Height=-height,Planes=1,BitCount=32};var pixels=new byte[width*height*4];
     if(Native.GetDIBits(dc,info.Color,0,(uint)height,pixels,ref header,0)==0)throw new System.ComponentModel.Win32Exception();
     for(int p=3;p<pixels.Length;p+=4)if(pixels[p]!=0){hasAlpha=true;break;}
    }finally{Native.DeleteDC(dc);}
   }
   return new(width,height,(int)info.HotX,(int)info.HotY,hasAlpha,Draw(handle,width,height,Color.Black),Draw(handle,width,height,Color.White));
  }finally{if(info.Mask!=0)Native.DeleteObject(info.Mask);if(info.Color!=0)Native.DeleteObject(info.Color);}
 }
 private static byte[] Draw(nint handle,int width,int height,Color background)
 {
  using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
  using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(background);var dc=graphics.GetHdc();try{if(!Native.DrawIconEx(dc,0,0,handle,width,height,0,0,3))throw new System.ComponentModel.Win32Exception();}finally{graphics.ReleaseHdc(dc);}}
  var data=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  try{var pixels=new byte[width*height*4];for(int y=0;y<height;y++)Marshal.Copy(data.Scan0+y*data.Stride,pixels,y*width*4,width*4);return pixels;}
  finally{bitmap.UnlockBits(data);}
 }
}

