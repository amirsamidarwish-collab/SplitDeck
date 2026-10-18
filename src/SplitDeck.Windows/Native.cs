using System.Runtime.InteropServices;

namespace SplitDeck.Windows;

internal static class Native
{
 internal const uint CurrentSettings=0xffffffff, Attached=1, Primary=4;
 internal const uint DmPosition=0x20,DmWidth=0x80000,DmHeight=0x100000,DmFrequency=0x400000,DmBits=0x40000;
 [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
 [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; public Rect(int x,int y,int w,int h){Left=x;Top=y;Right=x+w;Bottom=y+h;} }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct DisplayDevice {
  public int Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
  public static DisplayDevice New()=>new(){Size=Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key=""};
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct DevMode {
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
  public ushort SpecVersion,DriverVersion,Size,DriverExtra;public uint Fields;public int X,Y;public uint Orientation,FixedOutput;public short Color,Duplex,YResolution,TTOption,Collate;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string FormName;
  public ushort LogPixels;public uint BitsPerPel,Width,Height,Flags,Frequency,ICMMethod,ICMIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
  public static DevMode New()=>new(){Size=(ushort)Marshal.SizeOf<DevMode>(),Name="",FormName=""};
 }
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayDevices(string? device,uint index,ref DisplayDevice output,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplaySettingsEx(string device,uint mode,ref DevMode settings,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int ChangeDisplaySettingsEx(string? device,ref DevMode mode,nint hwnd,uint flags,nint param);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="ChangeDisplaySettingsExW")] internal static extern int CommitDisplaySettings(string? device,nint mode,nint hwnd,uint flags,nint param);
 [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] internal static extern bool ClipCursor(ref Rect rect);
 [DllImport("user32.dll",EntryPoint="ClipCursor")] internal static extern bool ReleaseCursor(nint rect);
 [DllImport("user32.dll")] internal static extern bool GetClipCursor(out Rect rect);
 [StructLayout(LayoutKind.Sequential)]internal struct CursorInfo {public int Size;public uint Flags;public nint Handle;public Point Position;}
 [DllImport("user32.dll",SetLastError=true)]internal static extern bool GetCursorInfo(ref CursorInfo info);
 // CURSORINFO is an in/out buffer. Windows can clear cbSize on a successful
 // query, so initialize it for every call rather than just once per loop.
 internal static bool TryGetCursorInfo(ref CursorInfo info){info.Size=Marshal.SizeOf<CursorInfo>();return GetCursorInfo(ref info);}
 [StructLayout(LayoutKind.Sequential)]internal struct IconInfo{[MarshalAs(UnmanagedType.Bool)]public bool IsIcon;public uint HotX,HotY;public nint Mask,Color;}
 [DllImport("user32.dll")]internal static extern bool GetIconInfo(nint icon,out IconInfo info);
 [DllImport("gdi32.dll")]internal static extern bool DeleteObject(nint obj);
 [DllImport("user32.dll")]internal static extern bool DrawIconEx(nint dc,int x,int y,nint icon,int width,int height,uint step,nint brush,uint flags);
 [StructLayout(LayoutKind.Sequential)]internal struct BitmapDescription{public int Type,Width,Height,WidthBytes;public ushort Planes,BitsPixel;public nint Bits;}
 [StructLayout(LayoutKind.Sequential)]internal struct BitmapInfoHeader{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPelsPerMeter,YPelsPerMeter;public uint ColorsUsed,ColorsImportant;}
 [DllImport("gdi32.dll",EntryPoint="GetObjectW")]internal static extern int GetBitmapObject(nint obj,int size,out BitmapDescription bitmap);
 [DllImport("gdi32.dll")]internal static extern nint CreateCompatibleDC(nint dc);
 [DllImport("gdi32.dll")]internal static extern bool DeleteDC(nint dc);
 [DllImport("gdi32.dll")]internal static extern int GetDIBits(nint dc,nint bitmap,uint first,uint lines,[Out]byte[] bits,ref BitmapInfoHeader info,uint usage);
 [DllImport("user32.dll")]internal static extern bool SetProcessDpiAwarenessContext(nint context);
 internal delegate nint WndProc(nint hwnd,uint msg,nuint wp,nint lp);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct WindowClass{public uint Size,Style;public WndProc Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;public string? Menu;public string ClassName;public nint SmallIcon;}
 [StructLayout(LayoutKind.Sequential)]internal struct Message{public nint Hwnd;public uint Msg;public nuint WParam;public nint LParam;public uint Time;public Point Point;public uint Private;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern ushort RegisterClassEx(ref WindowClass wc);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
 [DllImport("user32.dll")]internal static extern nint DefWindowProc(nint hwnd,uint msg,nuint wp,nint lp);
 [DllImport("user32.dll")]internal static extern bool PeekMessage(out Message msg,nint hwnd,uint min,uint max,uint remove);
 [DllImport("user32.dll")]internal static extern bool TranslateMessage(ref Message msg);
 [DllImport("user32.dll")]internal static extern nint DispatchMessage(ref Message msg);
 [DllImport("user32.dll")]internal static extern bool DestroyWindow(nint hwnd);
 [DllImport("user32.dll")]internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
 [DllImport("user32.dll")]internal static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(nint hwnd,int id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]internal static extern nint GetModuleHandle(string? name);
}
