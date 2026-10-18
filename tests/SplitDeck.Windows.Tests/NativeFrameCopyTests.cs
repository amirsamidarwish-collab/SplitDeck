using System.Runtime.InteropServices;
using SplitDeck.Windows;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

internal static class NativeFrameCopyTests
{
 internal static Task Equivalence()
 {
  D3D11.D3D11CreateDevice(null,DriverType.Warp,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out ID3D11Device device,out ID3D11DeviceContext context).CheckError();
  using(device)using(context){
   const int side=32;byte[] previous=new byte[side*side*4];for(int i=0;i<previous.Length;i++)previous[i]=(byte)(i*13+i/128);
   var cases=new[]{
    ("dirty",true,true,true,new RawRect[]{new(2,3,5,7)},Array.Empty<OutduplMoveRect>()),
    ("overlapping moves",true,true,true,Array.Empty<RawRect>(),new OutduplMoveRect[]{new(){SourcePoint=new(0,0),DestinationRect=new(6,6,16,16)},new(){SourcePoint=new(6,6),DestinationRect=new(1,1,11,11)}}),
    ("dirty over move",true,true,true,new RawRect[]{new(8,8,12,12)},new OutduplMoveRect[]{new(){SourcePoint=new(0,0),DestinationRect=new(6,6,16,16)}}),
    ("first/resize/recovery",false,true,true,new RawRect[]{new(2,3,5,7)},Array.Empty<OutduplMoveRect>()),
    ("rotation fallback",true,false,true,new RawRect[]{new(2,3,5,7)},Array.Empty<OutduplMoveRect>()),
    ("metadata fallback",true,true,false,Array.Empty<RawRect>(),Array.Empty<OutduplMoveRect>()),
    ("empty metadata fallback",true,true,true,Array.Empty<RawRect>(),Array.Empty<OutduplMoveRect>()),
    ("invalid rectangle fallback",true,true,true,new RawRect[]{new(-1,0,4,4)},Array.Empty<OutduplMoveRect>()),
    ("rectangle count fallback",true,true,true,Enumerable.Repeat(new RawRect(2,3,5,7),65).ToArray(),Array.Empty<OutduplMoveRect>()),
    ("coverage fallback",true,true,true,new RawRect[]{new(0,0,32,24)},Array.Empty<OutduplMoveRect>())};
   foreach(var(name,initialized,identity,metadata,dirty,moves)in cases){
    byte[] current=(byte[])previous.Clone();
    if(!initialized||name.Contains("fallback",StringComparison.Ordinal))for(int i=0;i<current.Length;i++)current[i]=(byte)(current[i]^0x9d);
    foreach(var move in moves){var r=move.DestinationRect;for(int y=0;y<r.Bottom-r.Top;y++)for(int x=0;x<r.Right-r.Left;x++)Buffer.BlockCopy(previous,((move.SourcePoint.Y+y)*side+move.SourcePoint.X+x)*4,current,((r.Top+y)*side+r.Left+x)*4,4);}
    foreach(var r in dirty){if(r.Left<0||r.Top<0||r.Right>side||r.Bottom>side)continue;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)for(int c=0;c<4;c++)current[(y*side+x)*4+c]=(byte)(31+c*53+x+y);}
    var description=new Texture2DDescription{Width=side,Height=side,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource};
    ID3D11Texture2D acquired,cached;var currentPin=GCHandle.Alloc(current,GCHandleType.Pinned);var previousPin=GCHandle.Alloc(previous,GCHandleType.Pinned);try{acquired=device.CreateTexture2D(description,new SubresourceData(currentPin.AddrOfPinnedObject(),side*4));cached=device.CreateTexture2D(description,new SubresourceData(previousPin.AddrOfPinnedObject(),side*4));}finally{currentPin.Free();previousPin.Free();}
    using(acquired)using(cached){
     RawRect[] destinations=new RawRect[64];var plan=FrameCopyPolicy.Plan(side,side,initialized,identity,metadata,dirty,moves,destinations);GpuFrameCopy.Execute(context,cached,acquired,plan,destinations);
     description.Usage=ResourceUsage.Staging;description.BindFlags=BindFlags.None;description.CPUAccessFlags=CpuAccessFlags.Read;using var staging=device.CreateTexture2D(description);context.CopyResource(staging,cached);var map=context.Map(staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
     try{byte[] row=new byte[side*4];for(int y=0;y<side;y++){Marshal.Copy(map.DataPointer+y*(int)map.RowPitch,row,0,row.Length);if(!row.AsSpan().SequenceEqual(current.AsSpan(y*side*4,side*4)))throw new Exception($"{name}: patched GPU image differs from the full acquired frame at row {y}.");}}
     finally{context.Unmap(staging,0);}
     Console.WriteLine($"GPU copy equivalence {name}: full={plan.FullCopy},rectangles={plan.RectangleCount},bytes={plan.CopyBytes}/{side*side*4}");
    }
   }
  }
  return Task.CompletedTask;
 }
}
