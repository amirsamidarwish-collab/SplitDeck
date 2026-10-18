using System.Management;
using System.Text;
using SplitDeck.Core;

namespace SplitDeck.Windows;

public sealed class DisplayService
{
 public IReadOnlyList<MonitorInfo> Detect()
 {
  var metadata=ReadMetadata();var result=new List<MonitorInfo>();
  for(uint i=0;;i++){
   var adapter=Native.DisplayDevice.New();if(!Native.EnumDisplayDevices(null,i,ref adapter,0))break;
   if((adapter.Flags&Native.Attached)==0)continue;
   var mode=Native.DevMode.New();if(!Native.EnumDisplaySettingsEx(adapter.Name,Native.CurrentSettings,ref mode,0)||mode.Width==0||mode.Height==0)continue;
   var target=Native.DisplayDevice.New();Native.EnumDisplayDevices(adapter.Name,0,ref target,1);
   var id=Normalize(target.Id);var meta=metadata.FirstOrDefault(m=>id.Contains(m.Id,StringComparison.OrdinalIgnoreCase));
   bool virt=adapter.Description.Contains("Virtual Display",StringComparison.OrdinalIgnoreCase)||adapter.Id.Contains("MttVDD",StringComparison.OrdinalIgnoreCase)||adapter.Description.Contains("Indirect",StringComparison.OrdinalIgnoreCase);
   string friendly=!string.IsNullOrWhiteSpace(meta?.Name)?meta.Name:!string.IsNullOrWhiteSpace(target.Description)?target.Description:adapter.Description;
   result.Add(new(adapter.Name,friendly,mode.X,mode.Y,(int)mode.Width,(int)mode.Height,mode.Frequency>1?mode.Frequency:60,meta?.Inches,(adapter.Flags&Native.Primary)!=0,virt));
  }
  return result;
 }
 internal static string Normalize(string text)=>text.Replace('#','\\').Replace("\\\\?\\","").Replace("_0","").ToUpperInvariant();
 internal static void RequireSupportedOutputRefresh(MonitorInfo physical,int hz)
 {
  for(uint index=0;index<4096;index++){var mode=Native.DevMode.New();if(!Native.EnumDisplaySettingsEx(physical.DeviceName,index,ref mode,0))break;if(mode.Width==physical.Width&&mode.Height==physical.Height&&mode.Frequency==hz)return;}
  throw new InvalidOperationException($"Windows does not advertise the selected physical resolution {physical.Width}×{physical.Height} at {hz} Hz. No display modes were changed.");
 }
 internal static List<DisplayAdapterState> AdapterInventory()
 {
  var adapters=new List<DisplayAdapterState>();
  for(uint i=0;;i++){var d=Native.DisplayDevice.New();if(!Native.EnumDisplayDevices(null,i,ref d,0))break;adapters.Add(new(d.Name,d.Id,d.Key,(d.Flags&Native.Attached)!=0));}
  return adapters;
 }
 internal static bool AttachInactiveOwned(IReadOnlyList<string> ownedKeys,IReadOnlyList<VirtualScreenLayout> layouts)
 {
  var current=Snapshot();if(!current.Any(c=>!string.Equals(c.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase)))return false;
  var anchor=current.OrderByDescending(c=>(long)c.X+c.Width).First();
  var plan=ManagedOutputAttachment.Plan(AdapterInventory(),ownedKeys,layouts,anchor);
  if(plan.Count==0)return false;
  var supported=new List<(OutputAttachment Request,int Hz)>();
  foreach(var request in plan){
   int? hz=null;
   for(uint modeIndex=0;modeIndex<4096;modeIndex++){var mode=Native.DevMode.New();if(!Native.EnumDisplaySettingsEx(request.DeviceName,modeIndex,ref mode,0))break;if(mode.Width==request.Width&&mode.Height==request.Height&&Math.Abs((double)mode.Frequency-request.Hz)<=1){hz=(int)mode.Frequency;break;}}
   if(hz is null)return false; // Driver monitor arrival/mode enumeration can be asynchronous.
   supported.Add((request,hz.Value));
  }
  foreach(var(request,hz)in supported)SetMode(request.DeviceName,request.X,request.Y,request.Width,request.Height,hz);
  Commit();return true;
 }
 private sealed record Meta(string Id,string Name,double? Inches);
 private static List<Meta> ReadMetadata()
 {
  var sizes=new Dictionary<string,double?>();var result=new List<Meta>();
  try{
   using(var query=new ManagementObjectSearcher("root\\wmi","SELECT InstanceName,MaxHorizontalImageSize,MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams"))
    foreach(ManagementObject m in query.Get()){using(m){double w=Convert.ToDouble(m["MaxHorizontalImageSize"]),h=Convert.ToDouble(m["MaxVerticalImageSize"]);sizes[Normalize((string)m["InstanceName"])]=w>0&&h>0?Math.Sqrt(w*w+h*h)/2.54:null;}}
   using(var query=new ManagementObjectSearcher("root\\wmi","SELECT InstanceName,UserFriendlyName FROM WmiMonitorID"))
    foreach(ManagementObject m in query.Get()){using(m){var id=Normalize((string)m["InstanceName"]);var name=m["UserFriendlyName"] is ushort[] a?new string(a.Where(x=>x!=0).Select(x=>(char)x).ToArray()):"";result.Add(new(id,name,sizes.GetValueOrDefault(id)));}}
  }catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}
  return result;
 }
 internal static List<DisplaySnapshot> Snapshot()
 {
  var list=new List<DisplaySnapshot>();
  for(uint i=0;;i++){var d=Native.DisplayDevice.New();if(!Native.EnumDisplayDevices(null,i,ref d,0))break;if((d.Flags&Native.Attached)==0)continue;var m=Native.DevMode.New();if(Native.EnumDisplaySettingsEx(d.Name,Native.CurrentSettings,ref m,0))list.Add(new(d.Name,d.Id,d.Key,m.X,m.Y,(int)m.Width,(int)m.Height,(int)m.Frequency,(d.Flags&Native.Primary)!=0));}
  return list;
 }
 internal static void SetMode(string name,int x,int y,int width,int height,int hz,bool primary=false)
 {
  var mode=Native.DevMode.New();Native.EnumDisplaySettingsEx(name,Native.CurrentSettings,ref mode,0);
  mode.Fields=Native.DmPosition|Native.DmWidth|Native.DmHeight|Native.DmFrequency|Native.DmBits;mode.X=x;mode.Y=y;mode.Width=(uint)width;mode.Height=(uint)height;mode.Frequency=(uint)hz;mode.BitsPerPel=32;
  int result=Native.ChangeDisplaySettingsEx(name,ref mode,0,0x10000001u|(primary?0x10u:0),0);
  if(result!=0)throw new InvalidOperationException($"Windows rejected display mode {width}×{height} at {hz} Hz on {name} (code {result}).");
 }
 internal static void Commit(){int r=Native.CommitDisplaySettings(null,0,0,0,0);if(r!=0)throw new InvalidOperationException($"Windows could not commit the layout (code {r}).");}
 internal static Native.DevMode DetachMode(DisplaySnapshot display){var mode=Native.DevMode.New();mode.Fields=Native.DmPosition|Native.DmWidth|Native.DmHeight;mode.X=display.X;mode.Y=display.Y;mode.Width=0;mode.Height=0;return mode;}
 internal static bool RestoreOwned(IReadOnlyList<DisplaySnapshot> original,IReadOnlyList<DisplaySnapshot> expected)
 {
  var current=Snapshot();bool HasOriginalIdentity(DisplaySnapshot c)=>original.Any(o=>string.Equals(o.AdapterId,c.AdapterId,StringComparison.OrdinalIgnoreCase)&&string.Equals(o.DeviceKey,c.DeviceKey,StringComparison.OrdinalIgnoreCase));
  var ownedKeys=current.Any(c=>!HasOriginalIdentity(c)&&string.Equals(c.AdapterId,"Root\\MttVDD",StringComparison.OrdinalIgnoreCase))?DriverInstallation.OwnedDisplayKeys():[];
  var plan=OwnedOutputRecovery.Plan(original,expected,current,ownedKeys);
  var errors=OwnedOutputRecovery.Execute(plan,original,saved=>{
   var matched=current.Single(c=>string.Equals(c.AdapterId,saved.AdapterId,StringComparison.OrdinalIgnoreCase)&&string.Equals(c.DeviceKey,saved.DeviceKey,StringComparison.OrdinalIgnoreCase));
   SetMode(matched.DeviceName,saved.X,saved.Y,saved.Width,saved.Height,saved.Hz,saved.Primary);
  },output=>{
  // A pending driver-disable restart need not leave an invisible desktop attached.
  // Detach only outputs mapped to our verified device instances, never foreign ones.
   var mode=DetachMode(output);int result=Native.ChangeDisplaySettingsEx(output.DeviceName,ref mode,0,0x10000001u,0);if(result!=0)throw new InvalidOperationException($"Windows could not detach managed display {output.DeviceName} (code {result}).");
  },Commit);
  var after=Snapshot();
  foreach(var output in plan.Detach)if(after.Any(c=>string.Equals(c.DeviceKey,output.DeviceKey,StringComparison.OrdinalIgnoreCase)))errors.Add($"Managed display {output.DeviceName} remains attached.");
  if(!plan.ReplayOriginal)errors.Add("External display change detected. Saved physical modes were not replayed. Review Windows Display Settings and the retained recovery journal.");
  else if(!TopologyGuard.CanRestore(original,original,after))errors.Add("Windows did not restore the exact original display topology.");
  if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
  return true;
 }
 internal static void Restore(IReadOnlyList<DisplaySnapshot> snapshots)
 {
  var current=Snapshot();var errors=new List<string>();
  foreach(var s in snapshots.OrderByDescending(s=>s.Primary)){
   var matched=current.FirstOrDefault(c=>c.AdapterId==s.AdapterId&&c.DeviceKey==s.DeviceKey);
   if(matched is null){errors.Add($"Display disconnected: {s.DeviceName}");continue;}
   try{SetMode(matched.DeviceName,s.X,s.Y,s.Width,s.Height,s.Hz,s.Primary);}catch(Exception e){errors.Add(e.Message);}
  }
  Commit();if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
 }
}
internal sealed record DisplaySnapshot(string DeviceName,string AdapterId,string DeviceKey,int X,int Y,int Width,int Height,int Hz,bool Primary);
