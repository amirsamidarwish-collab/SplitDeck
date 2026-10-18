using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SplitDeck.Core;

namespace SplitDeck.Windows;

internal static class DriverInstallation
{
 internal static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SplitDeck","Driver");
 private const string RegistryPath="SOFTWARE\\MikeTheTech\\VirtualDisplayDriver";
 private static string OwnerPath=>Path.Combine(Root,"owner.json");
 private static string RestartPath=>Path.Combine(Root,"restart-required.json");
 internal static string SettingsPath=>Path.Combine(Root,"vdd_settings.xml");
 private sealed record Owner(string DeviceId,string Version,string[]? AdditionalDeviceIds=null);
 internal static string[] ParseOwnedDevices(string json)
 {
  try{var o=JsonSerializer.Deserialize<Owner>(json);if(o is null||o.Version!="25.7.23")return [];var ids=new[]{o.DeviceId}.Concat(o.AdditionalDeviceIds??[]).ToArray();return ids.Length is >=1 and <=3&&ids.All(id=>!string.IsNullOrWhiteSpace(id)&&id.Length<512&&id.StartsWith("ROOT\\",StringComparison.OrdinalIgnoreCase))&&ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()==ids.Length?ids:[];}catch(JsonException){return [];}
 }
 internal static string[] OwnedDeviceIds{get{try{return ParseOwnedDevices(File.ReadAllText(OwnerPath));}catch(IOException){return [];}}}
 internal static string? OwnedDeviceId=>OwnedDeviceIds.FirstOrDefault();
 internal static string[] OwnedDisplayKeys(int? count=null)
 {
  var ids=OwnedDeviceIds;
  if(!ExclusivelyOwned(ids,Devices())||ids.Any(id=>{var health=InspectHealth(id);return !health.PackageVerified||health.ProblemCode is not(0 or 14 or 22);}))throw new InvalidOperationException("Cannot prove ownership and trusted binding of the virtual display instances. No display detach was attempted.");
  using(var configuration=Registry.LocalMachine.OpenSubKey(RegistryPath))if(!string.Equals(configuration?.GetValue("VDDPATH") as string,Root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The virtual driver configuration belongs to another installation. No display detach was attempted.");
  var keys=new List<string>();
  if(count is int requested&&(requested is <1 or >3||requested>ids.Length))throw new ArgumentOutOfRangeException(nameof(count));
  foreach(string id in ids.Take(count??ids.Length)){using var parameters=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\"+id+"\\Device Parameters");if(parameters?.GetValue("VideoID") is string video&&Guid.TryParse(video,out var guid))keys.Add("\\Registry\\Machine\\System\\CurrentControlSet\\Control\\Video\\"+guid.ToString("B").ToUpperInvariant()+"\\0000");}
  return keys.ToArray();
 }
 internal static bool ExclusivelyOwned(string[] owned,string[] present)=>owned.Length is >=1 and <=3&&owned.Distinct(StringComparer.OrdinalIgnoreCase).Count()==owned.Length&&present.Length==owned.Length&&present.All(id=>owned.Contains(id,StringComparer.OrdinalIgnoreCase));
 internal static bool Installed
 {
  get=>InstalledForCount(2);
 }
 internal static bool InstalledForCount(int count){if(count is not(2 or 3))throw new ArgumentOutOfRangeException(nameof(count));RequireNoPendingRestart();var ids=OwnedDeviceIds;return ids.Length>=count&&ids.All(Healthy)&&ExclusivelyOwned(ids,Devices());}
 private static void RequireNoPendingRestart(){if(RestartPending)throw new DriverRestartRequiredException("Windows requires a restart to finish disabling the managed virtual driver. Apply is blocked until restart or every managed device is verified disabled. The recovery journal has been retained.");}
 internal static bool RestartPending
 {
  get{
   if(!File.Exists(RestartPath))return false;
   try{var state=JsonSerializer.Deserialize<DriverRestartState>(File.ReadAllText(RestartPath));if(state is null)return true;return DriverRestartState.Pending(state.BootId,ReadBootIdentity(),OwnedDeviceIds.Select(ProblemCode).ToArray());}
   catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return true;}
  }
 }
 private static string? ReadBootIdentity()
 {
  try{using var query=new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");foreach(ManagementObject os in query.Get())using(os)return os["LastBootUpTime"] as string;}
  catch(Exception e)when(e is ManagementException or System.Runtime.InteropServices.COMException){}
  return null;
 }
 private static int? ProblemCode(string id)=>CM_Locate_DevNode(out uint devinst,id,0)==0&&CM_Get_DevNode_Status(out _,out uint code,devinst,0)==0?(int)code:null;
 private static void ClearResolvedRestart(){if(File.Exists(RestartPath)&&!RestartPending)File.Delete(RestartPath);}
 internal static void RecordRestartRequired(string detail)=>AtomicWrite(RestartPath,JsonSerializer.Serialize(new DriverRestartState(ReadBootIdentity(),DateTime.UtcNow,detail)));
 private static bool Healthy(string id)=>InspectHealth(id).Ready;
 internal static DriverHealthState InspectHealth(string id)
 {
  try{
   var binding=ReadBoundDriver(id);
   if(binding.Version!="11.30.4.434"||binding.Provider!="MikeTheTech")return new(false,null,"The device is bound to an unexpected driver version or provider.");
   DriverVerification.VerifyPackage(DriverVerification.DriverStoreFolder(binding.Inf));
   int? problem=null;
   if(CM_Locate_DevNode(out uint devinst,id,0)==0&&CM_Get_DevNode_Status(out _,out uint code,devinst,0)==0)problem=(int)code;
   return new(true,problem,$"Native bound package, Windows signature and pinned installed-package checks passed. Device problem code: {problem?.ToString()??"not present"}.");
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException){return new(false,null,e.Message);}
 }
 internal sealed record BoundDriver(string Provider,string Version,string Inf);
 internal static BoundDriver ReadBoundDriver(string id)
 {
  var cls=new Guid("4d36e968-e325-11ce-bfc1-08002be10318");nint set=SetupDiCreateDeviceInfoList(ref cls,0);if(set==-1)throw new Win32Exception(Marshal.GetLastWin32Error());
  try{
   var data=new DeviceInfo{Size=Marshal.SizeOf<DeviceInfo>()};if(!SetupDiOpenDeviceInfo(set,id,0,0,ref data))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot open the bound display device.");
   nint nativeKey=SetupDiOpenDevRegKey(set,ref data,1,0,2,0x20019);if(nativeKey==-1)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot read the device's bound driver key.");
   using var handle=new SafeRegistryHandle(nativeKey,true);using var key=RegistryKey.FromHandle(handle);
   return new(key.GetValue("ProviderName") as string??"",key.GetValue("DriverVersion") as string??"",key.GetValue("InfPath") as string??"");
  }finally{SetupDiDestroyDeviceInfoList(set);}
 }
 internal static async Task<DriverHealthState> WaitForHealthAsync(Func<DriverHealthState> inspect,TimeSpan timeout,bool requireDisabled=false)
 {
  var deadline=DateTime.UtcNow+timeout;DriverHealthState state;
  do{state=inspect();if((state.Ready&&(!requireDisabled||state.ProblemCode==22))||state.RestartRequired)return state;if(DateTime.UtcNow>=deadline)break;await Task.Delay(300);}while(true);
  return state;
 }
 private static string[] Devices()
 {
  var ids=new List<string>();using var q=new ManagementObjectSearcher("SELECT PNPDeviceID,HardwareID FROM Win32_PnPEntity WHERE PNPClass='Display'");
  foreach(ManagementObject d in q.Get()){using(d){if(d["HardwareID"] is string[] hw&&hw.Any(x=>x.Contains("MttVDD",StringComparison.OrdinalIgnoreCase)))ids.Add((string)d["PNPDeviceID"]);}}
  return ids.ToArray();
 }
 internal static void RequireOwned(bool requirePair)=>RequireOwned(requirePair?2:1);
 internal static void RequireOwned(int count=2)
 {
  RequireNoPendingRestart();
  if(count is <1 or >3)throw new ArgumentOutOfRangeException(nameof(count));
  var ids=OwnedDeviceIds;var all=Devices();if(ids.Length<count||!ids.All(Healthy))throw new InvalidOperationException("Run Setup driver to prepare one managed driver instance per screen.");
  if(!ExclusivelyOwned(ids,all))throw new InvalidOperationException("Another Virtual Display Driver installation is present. SplitDeck will not change another app’s virtual displays.");
  using var key=Registry.LocalMachine.OpenSubKey(RegistryPath);
  if(!string.Equals(key?.GetValue("VDDPATH") as string,Root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The virtual driver configuration belongs to another installation. No changes were made.");
 }
 internal static async Task InstallAsync(int count=2)
 {
  if(count is not(2 or 3))throw new ArgumentOutOfRangeException(nameof(count));
  if(OwnedDeviceIds.Length>0){RequireOwned(false);if(OwnedDeviceIds.Length<count)await UpgradeInstancesAsync(count);await EnsureDisabledAsync();return;}
  if(Devices().Length>0)throw new InvalidOperationException("An existing Virtual Display Driver was found. SplitDeck keeps it untouched. Use a clean driver setup before installing SplitDeck’s managed driver.");
  using(var existing=Registry.LocalMachine.OpenSubKey(RegistryPath))if(existing?.GetValue("VDDPATH") is not null)throw new InvalidOperationException("An existing VDDPATH configuration was found. SplitDeck will not overwrite it.");
  string package=Path.Combine(AppContext.BaseDirectory,"assets","driver");
  DriverVerification.VerifyPackage(package);
  Directory.CreateDirectory(Root);
  if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("The driver folder must not be a symbolic link.");
  var profile=LayoutEngine.Calculate(Presets.Balanced(60),1920,1080,60);AtomicWrite(SettingsPath,DriverConfiguration.Build(profile).ToString());
  using(var key=Registry.LocalMachine.CreateSubKey(RegistryPath)){key.SetValue("VDDPATH",Root,RegistryValueKind.String);}
  string? created=null;
  await InstallationAttempt.RunAsync(async()=>{
   created=CreateDevice();AtomicWrite(OwnerPath,JsonSerializer.Serialize(new Owner(created,"25.7.23")));
   if(!UpdateDriverForPlugAndPlayDevices(0,"Root\\MttVDD",Path.Combine(package,"MttVDD.inf"),1,out bool reboot))throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not install the signed virtual display driver.");
   var health=await WaitForHealthAsync(()=>InspectHealth(created),TimeSpan.FromSeconds(15));
   if(!health.Ready)throw new InvalidOperationException($"Driver verification failed for {created}. {health.Detail}"+(health.RestartRequired||reboot?" Windows requested a restart. Restart Windows, then run Setup driver again.":" See C:\\Windows\\INF\\setupapi.dev.log for Windows installation details."));
   await DisableAsync();if(reboot)throw new InvalidOperationException("Windows installed the driver and requested a restart. Restart Windows, then run Setup driver again.");
  },()=>FirstInstallRollback.RunAsync(created,id=>RunAsync("pnputil.exe",["/remove-device",id]),async id=>(await RunStateOperationAsync(id,false)).RequireSuccess(),
   ()=>{using var key=Registry.LocalMachine.OpenSubKey(RegistryPath,true);if(string.Equals(key?.GetValue("VDDPATH") as string,Root,StringComparison.OrdinalIgnoreCase))key?.DeleteValue("VDDPATH",false);return Task.CompletedTask;},
   ()=>{File.Delete(OwnerPath);File.Delete(SettingsPath);return Task.CompletedTask;}));
  await UpgradeInstancesAsync(count);
  await EnsureDisabledAsync();
 }
 private static async Task EnsureDisabledAsync()
 {
  await DisableAsync();foreach(string id in OwnedDeviceIds){var health=await WaitForHealthAsync(()=>InspectHealth(id),TimeSpan.FromSeconds(10),requireDisabled:true);if(!health.Ready||health.ProblemCode!=22)throw new InvalidOperationException($"Setup could not confirm disabled device {id}. {health.Detail}");}
 }
 private static async Task UpgradeInstancesAsync(int count)
 {
  RequireOwned(false);string[] existing=OwnedDeviceIds;
  if(new DisplayService().Detect().Any(x=>x.IsVirtual))throw new InvalidOperationException("Restore active virtual displays before upgrading driver setup.");
  string package=Path.Combine(AppContext.BaseDirectory,"assets","driver");DriverVerification.VerifyPackage(package);
  string? previousSettings=File.Exists(SettingsPath)?await File.ReadAllTextAsync(SettingsPath):null;
  var defaults=LayoutEngine.Calculate(Presets.Balanced(60),1920,1080,60);AtomicWrite(SettingsPath,DriverConfiguration.Build(defaults).ToString());
  try{await DriverPairUpgrade.RunAsync(existing,count,CreateDevice,ids=>AtomicWrite(OwnerPath,JsonSerializer.Serialize(new Owner(ids[0],"25.7.23",ids.Skip(1).ToArray()))),async()=>{
   if(!UpdateDriverForPlugAndPlayDevices(0,"Root\\MttVDD",Path.Combine(package,"MttVDD.inf"),1,out bool reboot))throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not bind the additional signed driver instance.");
   foreach(string id in OwnedDeviceIds){var health=await WaitForHealthAsync(()=>InspectHealth(id),TimeSpan.FromSeconds(15));if(!health.Ready)throw new InvalidOperationException($"Managed driver verification failed for {id}. {health.Detail}");}
   if(reboot)throw new InvalidOperationException("Windows requested a restart for the managed driver instances. Restart and run Setup driver again.");
  },async id=>(await RunStateOperationAsync(id,false)).RequireSuccess(),id=>RunAsync("pnputil.exe",["/remove-device",id]));}
  catch{if(previousSettings is not null)AtomicWrite(SettingsPath,previousSettings);throw;}
 }
 internal static async Task EnableAsync(int count=2){RequireOwned(count);ClearResolvedRestart();await DriverActivation.RunAsync(OwnedDeviceIds,count,async(id,enable)=>(await RunStateOperationAsync(id,enable)).RequireSuccess());}
 internal static async Task DisableAsync(){if(RestartPending)throw new DriverRestartRequiredException("Windows still requires a restart to disable the managed virtual driver; repeated device-disable attempts were skipped. Recovery will still attempt to detach managed virtual outputs safely.");await DriverDisable.RunAsync(OwnedDeviceIds,id=>RunStateOperationAsync(id,false),ProblemCode);ClearResolvedRestart();}
 private static async Task<DriverOperationResult> RunStateOperationAsync(string id,bool enable)
 {
  var result=await DriverStateTransition.RunAsync(enable,()=>RunOperationAsync("pnputil.exe",[enable?"/enable-device":"/disable-device",id]),requested=>NativeDeviceState.Change(id,requested),()=>InspectHealth(id),TimeSpan.FromSeconds(15));
  if(DriverOperationResult.RestartRequired(result.ExitCode))RecordRestartRequired($"Managed device {id}: {result.Output}");
  return result;
 }
 internal static void Configure(IReadOnlyList<VirtualScreenLayout> screens){RequireOwned(screens.Count);AtomicWrite(SettingsPath,DriverConfiguration.Build(screens).ToString());}
 internal static void AtomicWrite(string path,string text){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text,Encoding.UTF8);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 internal static async Task<string> RunAsync(string file,string[] arguments)
 =>(await RunOperationAsync(file,arguments)).RequireSuccess();
 private static async Task<DriverOperationResult> RunOperationAsync(string file,string[] arguments)
 {
  var start=new ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string a in arguments)start.ArgumentList.Add(a);
  using var process=Process.Start(start)??throw new IOException("Could not launch Windows driver utility.");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));try{await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){try{process.Kill(true);}catch{}throw new TimeoutException("Windows driver utility timed out.");}
  string output=(await stdout)+"\n"+(await stderr);var result=new DriverOperationResult(process.ExitCode,output);
  if(DriverOperationResult.RestartRequired(result.ExitCode))RecordRestartRequired($"{file} {string.Join(' ',arguments)}: {result.ExitCode}. {output.Trim()}");
  return result;
 }
 [StructLayout(LayoutKind.Sequential)]private struct DeviceInfo{public int Size;public Guid ClassGuid;public uint DevInst;public nint Reserved;}
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]private static extern uint CM_Locate_DevNode(out uint devinst,string id,uint flags);
 [DllImport("cfgmgr32.dll")]private static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint devinst,uint flags);
 [DllImport("setupapi.dll",SetLastError=true)]private static extern nint SetupDiCreateDeviceInfoList(ref Guid cls,nint hwnd);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiOpenDeviceInfo(nint set,string instanceId,nint hwnd,uint flags,ref DeviceInfo data);
 [DllImport("setupapi.dll",SetLastError=true)]private static extern nint SetupDiOpenDevRegKey(nint set,ref DeviceInfo data,uint scope,uint profile,uint type,uint access);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiCreateDeviceInfo(nint set,string name,ref Guid cls,string desc,nint hwnd,uint flags,ref DeviceInfo data);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiSetDeviceRegistryProperty(nint set,ref DeviceInfo data,uint property,byte[] buffer,uint size);
 [DllImport("setupapi.dll",SetLastError=true)]private static extern bool SetupDiCallClassInstaller(uint function,nint set,ref DeviceInfo data);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiGetDeviceInstanceId(nint set,ref DeviceInfo data,StringBuilder id,uint size,out uint needed);
 [DllImport("setupapi.dll")]private static extern bool SetupDiDestroyDeviceInfoList(nint set);
 [DllImport("newdev.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool UpdateDriverForPlugAndPlayDevices(nint hwnd,string hardware,string inf,uint flags,out bool reboot);
 private static string CreateDevice()
 {
  var cls=new Guid("4d36e968-e325-11ce-bfc1-08002be10318");nint set=SetupDiCreateDeviceInfoList(ref cls,0);if(set==-1)throw new Win32Exception();
  try{var d=new DeviceInfo{Size=Marshal.SizeOf<DeviceInfo>()};if(!SetupDiCreateDeviceInfo(set,"Display",ref cls,"SplitDeck managed virtual displays",0,1,ref d))throw new Win32Exception(Marshal.GetLastWin32Error());
   byte[] hw=Encoding.Unicode.GetBytes("Root\\MttVDD\0\0");if(!SetupDiSetDeviceRegistryProperty(set,ref d,1,hw,(uint)hw.Length)||!SetupDiCallClassInstaller(0x19,set,ref d))throw new Win32Exception(Marshal.GetLastWin32Error());
   var name=new StringBuilder(512);if(!SetupDiGetDeviceInstanceId(set,ref d,name,512,out _))throw new Win32Exception(Marshal.GetLastWin32Error());return name.ToString();
  }finally{SetupDiDestroyDeviceInfoList(set);}
 }
}
