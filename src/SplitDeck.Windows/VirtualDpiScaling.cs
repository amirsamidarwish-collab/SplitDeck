using System.ComponentModel;
using System.Runtime.InteropServices;
using SplitDeck.Core;

namespace SplitDeck.Windows;

internal sealed record VirtualDpiChange(string DeviceKey,string DeviceName,int OriginalRelative,int AppliedRelative,int Percent,double RenderDensity=1,bool Restored=false);

// Windows stores scale per display source. Only owned virtual sources are changed;
// the physical monitor supplies the reference DPI and is never written here.
internal static class VirtualDpiScaling
{
 private static readonly int[] Percentages=[100,125,150,175,200,225,250,300,350,400,450,500];

 internal static int PhysicalDpi(MonitorInfo monitor)
 {
  nint previous=SetThreadDpiAwarenessContext(-4);
  if(previous==0)throw new InvalidOperationException("Cannot read the physical monitor's DPI safely.");
  nint probe=0;
  try{
   // An invisible, per-monitor-aware window gives the effective DPI through the
   // supported GetDpiForWindow API, including the user's own scaling choice.
   probe=Native.CreateWindowEx(0x08000080,"STATIC","",0x80000000,monitor.X+monitor.Width/2,monitor.Y+monitor.Height/2,1,1,0,0,Native.GetModuleHandle(null),0);
   uint dpi=probe==0?0:GetDpiForWindow(probe);
   if(dpi is <48 or >768)throw new InvalidOperationException("Cannot establish the physical monitor's desktop scaling.");
   return (int)dpi;
  }finally{if(probe!=0)Native.DestroyWindow(probe);SetThreadDpiAwarenessContext(previous);}
 }

 internal static List<VirtualDpiChange> Plan(IReadOnlyList<MonitorInfo> virtuals,IReadOnlyList<VirtualScreenLayout> layouts,int physicalDpi)
 {
  if(virtuals.Count!=layouts.Count||physicalDpi is <48 or >768)throw new ArgumentException("A matching virtual layout and physical DPI are required.");
  var owned=DriverInstallation.OwnedDisplayKeys(virtuals.Count);
  var displays=DisplayService.Snapshot();var sources=Sources();var changes=new List<VirtualDpiChange>();
  for(int i=0;i<virtuals.Count;i++){
   var monitor=virtuals[i];var layout=layouts[i];
   var display=displays.Single(d=>d.DeviceName==monitor.DeviceName&&string.Equals(d.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase)&&owned.Contains(d.DeviceKey,StringComparer.OrdinalIgnoreCase));
   if(!monitor.IsVirtual)throw new InvalidOperationException("Desktop scaling is restricted to SplitDeck's virtual displays.");
   var source=sources.Single(s=>s.Name==monitor.DeviceName);var scale=Read(source);
   int recommended=checked(-scale.Minimum);
   if(scale.Minimum>0||scale.Maximum<0||recommended>=Percentages.Length||recommended+scale.Maximum>=Percentages.Length||scale.Current<scale.Minimum||scale.Current>scale.Maximum)
    throw new InvalidOperationException("Windows returned an unsupported virtual display scaling range.");
   double density=Math.Sqrt((double)layout.RenderWidth/layout.VisibleBounds.Width*layout.RenderHeight/layout.VisibleBounds.Height);
   if(!double.IsFinite(density)||density is <1d/16 or >16)throw new InvalidOperationException("This render resolution is too far from the visible region for consistent desktop sizing. Choose render dimensions within 16 times the visible size.");
   double desired=physicalDpi/96d*100*density;
   int absolute=Enumerable.Range(0,recommended+scale.Maximum+1).MinBy(index=>Math.Abs(Percentages[index]-desired));
   int relative=absolute-recommended;
   changes.Add(new(display.DeviceKey,display.DeviceName,scale.Current,relative,Percentages[absolute],density));
  }
  return changes;
 }

 internal static async Task ApplyAsync(IReadOnlyList<VirtualDpiChange> changes,bool restoring=false,Action<VirtualDpiChange>? onRestored=null)
 {
  if(restoring)changes=changes.Where(c=>!c.Restored&&c.OriginalRelative!=c.AppliedRelative).ToArray();
  if(changes.Count==0)return;
  var owned=DriverInstallation.OwnedDisplayKeys();var displays=DisplayService.Snapshot();var sources=Sources();
  foreach(var change in changes){
   var display=displays.SingleOrDefault(d=>string.Equals(d.DeviceKey,change.DeviceKey,StringComparison.OrdinalIgnoreCase)&&string.Equals(d.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase));
   if(display is null)throw new InvalidOperationException(restoring?"A virtual DPI restoration is still pending because its source is unavailable. The recovery journal was retained.":"The virtual display disconnected before scaling was configured.");
   if(!owned.Contains(display.DeviceKey,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Cannot prove ownership of a display scaling change.");
   var source=sources.Single(s=>s.Name==display.DeviceName);var current=Read(source);
   int requested=restoring?change.OriginalRelative:change.AppliedRelative;
   if(current.Current==requested){if(restoring)onRestored?.Invoke(change);continue;}
   // Do not replace a later user scaling edit with stale recovery state.
   if(restoring&&current.Current!=change.AppliedRelative){onRestored?.Invoke(change);continue;}
   if(requested<current.Minimum||requested>current.Maximum)throw new InvalidOperationException("The saved virtual DPI is outside Windows' current supported range.");
   var packet=new ScaleSet{Header=Header.For(-4,24,source),Relative=requested};
   int result=DisplayConfigSetDeviceInfo(ref packet);
   if(result!=0)throw new Win32Exception(result,"Windows could not apply consistent scaling to "+display.DeviceName+".");
   bool verified=false;
   for(int attempt=0;attempt<12;attempt++){if(Read(source).Current==requested){verified=true;break;}await Task.Delay(50);}
   if(!verified)throw new InvalidOperationException("Windows did not confirm the virtual display scaling change.");
   if(restoring)onRestored?.Invoke(change);
  }
 }

 private static ScaleGet Read(Source source)
 {
  // Source-DPI packet types -3/-4 are used by Windows Settings but are not part
  // of the documented SDK contract. Validate ABI/ranges and fail before session
  // activation if this Windows version rejects them; never edit DPI registry keys.
  if(Marshal.SizeOf<ScaleGet>()!=32||Marshal.SizeOf<ScaleSet>()!=24)throw new InvalidOperationException("Unsupported display scaling packet layout.");
  var packet=new ScaleGet{Header=Header.For(-3,32,source)};int result=DisplayConfigGetDeviceInfo(ref packet);
  if(result!=0)throw new Win32Exception(result,"Windows does not expose per-display scaling for "+source.Name+".");
  return packet;
 }

 private sealed record Source(string Name,Luid Adapter,uint Id);
 private static List<Source> Sources()
 {
  const uint active=2;
  for(int attempt=0;attempt<4;attempt++){
   int result=GetDisplayConfigBufferSizes(active,out uint pathCount,out uint modeCount);
   if(result!=0)throw new Win32Exception(result);
   if(pathCount>256||modeCount>1024)throw new InvalidOperationException("Unexpected display configuration size.");
   var paths=new PathInformation[pathCount];nint modes=Marshal.AllocHGlobal(checked((int)modeCount*64));
   try{
    result=QueryDisplayConfig(active,ref pathCount,paths,ref modeCount,modes,0);
    if(result==122)continue;
    if(result!=0)throw new Win32Exception(result);
    var sources=new List<Source>();
    foreach(var path in paths.Take((int)pathCount)){
     var source=new Source("",path.Source.Adapter,path.Source.Id);
     var name=new SourceName{Header=Header.For(1,84,source),Name=""};
     result=DisplayConfigGetDeviceInfo(ref name);if(result!=0)throw new Win32Exception(result);
     if(!sources.Any(s=>s.Name==name.Name))sources.Add(source with{Name=name.Name});
    }
    return sources;
   }finally{Marshal.FreeHGlobal(modes);}
  }
  throw new InvalidOperationException("Display configuration kept changing while scaling was prepared.");
 }

 [StructLayout(LayoutKind.Sequential)]private struct Luid{internal uint Low;internal int High;}
 [StructLayout(LayoutKind.Sequential)]private struct Header{
  internal int Type;internal uint Size;internal Luid Adapter;internal uint Id;
  internal static Header For(int type,uint size,Source source)=>new(){Type=type,Size=size,Adapter=source.Adapter,Id=source.Id};
 }
 [StructLayout(LayoutKind.Sequential)]private struct ScaleGet{internal Header Header;internal int Minimum,Current,Maximum;}
 [StructLayout(LayoutKind.Sequential)]private struct ScaleSet{internal Header Header;internal int Relative;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct SourceName{internal Header Header;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]internal string Name;}
 [StructLayout(LayoutKind.Sequential)]private struct SourceInformation{internal Luid Adapter;internal uint Id,ModeIndex,Status;}
 [StructLayout(LayoutKind.Sequential)]private struct Rational{internal uint Numerator,Denominator;}
 [StructLayout(LayoutKind.Sequential)]private struct TargetInformation{
  internal Luid Adapter;internal uint Id,ModeIndex,Technology,Rotation,Scaling;internal Rational Refresh;internal uint Scanline;internal int Available;internal uint Status;
 }
 [StructLayout(LayoutKind.Sequential)]private struct PathInformation{internal SourceInformation Source;internal TargetInformation Target;internal uint Flags;}
 [DllImport("user32.dll")]private static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
 [DllImport("user32.dll")]private static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out]PathInformation[] pathArray,ref uint modes,nint modeArray,nint topology);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int DisplayConfigGetDeviceInfo(ref SourceName packet);
 [DllImport("user32.dll")]private static extern int DisplayConfigGetDeviceInfo(ref ScaleGet packet);
 [DllImport("user32.dll")]private static extern int DisplayConfigSetDeviceInfo(ref ScaleSet packet);
 [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint context);
 [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint window);
}
