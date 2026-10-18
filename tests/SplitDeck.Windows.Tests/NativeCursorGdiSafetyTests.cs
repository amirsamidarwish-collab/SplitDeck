using System.Runtime.InteropServices;
using SplitDeck.Windows;

internal static class NativeCursorGdiSafetyTests
{
 internal static Task BitmapInfoBounds()
 {
  if(!Native.GetIconInfo(System.Windows.Forms.Cursors.Arrow.Handle,out var icon))throw new System.ComponentModel.Win32Exception();
  try{
   if(icon.Color==0)throw new Exception("This regression needs the machine's real color arrow.");
   if(Native.GetBitmapObject(icon.Color,Marshal.SizeOf<Native.BitmapDescription>(),out var bitmap)==0)throw new System.ComponentModel.Win32Exception();
   int headerSize=Marshal.SizeOf<Native.BitmapInfoHeader>();nint buffer=Marshal.AllocHGlobal(128);nint dc=Native.CreateCompatibleDC(0);
   try{
    for(int i=0;i<128;i++)Marshal.WriteByte(buffer,i,0xcd);
    var header=new Native.BitmapInfoHeader{Size=(uint)headerSize,Width=bitmap.Width,Height=-bitmap.Height,Planes=1,BitCount=32};Marshal.StructureToPtr(header,buffer,false);
    int result=GetDIBits(dc,icon.Color,0,(uint)bitmap.Height,new byte[bitmap.Width*bitmap.Height*4],buffer,0);
    var returned=Marshal.PtrToStructure<Native.BitmapInfoHeader>(buffer);int lastWritten=headerSize-1;
    for(int i=headerSize;i<128;i++)if(Marshal.ReadByte(buffer,i)!=0xcd)lastWritten=i;
    Console.WriteLine($"Arrow GetDIBits result={result}, headerSize={headerSize}, returnedCompression={returned.Compression}, lastWrittenOffset={lastWritten}");
    if(lastWritten>=headerSize)throw new Exception($"GetDIBits writes beyond the current {headerSize}-byte BITMAPINFOHEADER declaration, through offset {lastWritten}.");
   }finally{if(dc!=0)Native.DeleteDC(dc);Marshal.FreeHGlobal(buffer);}
  }finally{if(icon.Mask!=0)Native.DeleteObject(icon.Mask);if(icon.Color!=0)Native.DeleteObject(icon.Color);}
  return Task.CompletedTask;
 }
 internal static Task CursorInfoAfterRaster()
 {
  var cursor=new Native.CursorInfo();
  for(int i=0;i<12;i++){
   bool before=Native.TryGetCursorInfo(ref cursor);Console.WriteLine($"CursorInfo before raster{i}: result={before},size={cursor.Size},handle={cursor.Handle}");
   if(!before)throw new Exception("Cursor query wrapper must initialize the required size before every call.");
   NativeCursorRaster.Read(System.Windows.Forms.Cursors.Arrow.Handle);
   bool after=Native.TryGetCursorInfo(ref cursor);Console.WriteLine($"CursorInfo after raster{i}: result={after},size={cursor.Size},handle={cursor.Handle}");
   if(!after)throw new Exception("Cursor query wrapper stopped succeeding after rasterization.");
   cursor.Size=0; // Output cbSize is not promised; emulate it being overwritten on any Windows version.
  }
  return Task.CompletedTask;
 }
 [DllImport("gdi32.dll")]private static extern int GetDIBits(nint dc,nint bitmap,uint first,uint lines,[Out]byte[] bits,nint info,uint usage);
}
