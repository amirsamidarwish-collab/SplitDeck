using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SplitDeck.Windows;

// Class-installer path for legacy root devices which PnPUtil reports disconnected.
// Pattern and parameter scopes follow Microsoft's DevCon ControlCallback.
internal static class NativeDeviceState
{
 internal static DriverOperationResult Change(string id,bool enable)
 {
  if(!EditorElevation.IsElevated)throw new InvalidOperationException("Changing a managed device requires Windows administrator approval.");
  DriverInstallation.RequireOwned(false);
  if(!DriverInstallation.OwnedDeviceIds.Contains(id,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("The requested device does not belong to SplitDeck.");
  var cls=new Guid("4d36e968-e325-11ce-bfc1-08002be10318");nint set=SetupDiCreateDeviceInfoList(ref cls,0);
  if(set==-1)throw new Win32Exception(Marshal.GetLastWin32Error());
  try{
   var device=new DeviceInfo{Size=Marshal.SizeOf<DeviceInfo>()};
   if(!SetupDiOpenDeviceInfo(set,id,0,0,ref device))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot open the managed device for a state change.");
   bool Apply(uint scope){var change=Parameters(enable,scope);return SetupDiSetClassInstallParams(set,ref device,ref change,(uint)Marshal.SizeOf<PropertyChangeParameters>())&&SetupDiCallClassInstaller(0x12,set,ref device);}
   uint Flags(){var parameters=new DeviceInstallParameters{Size=(uint)Marshal.SizeOf<DeviceInstallParameters>(),DriverPath=""};if(!SetupDiGetDeviceInstallParams(set,ref device,ref parameters))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot verify device state-change restart requirements.");return parameters.Flags;}
   if(enable&&Apply(1)&&(Flags()&0x180)!=0)return new(3010,"The global managed-device enable requires Windows restart.");
   if(!Apply(2))return new(Marshal.GetLastWin32Error(),"Windows class installer could not "+(enable?"enable":"disable")+" the managed device "+id+".");
   return (Flags()&0x180)!=0?new(3010,"The managed-device state change requires Windows restart."):new(0,"Windows class installer changed managed device "+id+".");
  }finally{SetupDiDestroyDeviceInfoList(set);}
 }
 internal static PropertyChangeParameters Parameters(bool enable,uint scope)=>new(){HeaderSize=8,InstallFunction=0x12,StateChange=enable?1u:2u,Scope=scope,HwProfile=0};
 [StructLayout(LayoutKind.Sequential)]internal struct PropertyChangeParameters{internal uint HeaderSize,InstallFunction,StateChange,Scope,HwProfile;}
 [StructLayout(LayoutKind.Sequential)]private struct DeviceInfo{internal int Size;internal Guid ClassGuid;internal uint DevInst;internal nint Reserved;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct DeviceInstallParameters{internal uint Size,Flags,FlagsEx;internal nint Parent,MessageHandler,MessageContext,FileQueue,ClassReserved;internal uint Reserved;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]internal string DriverPath;}
 [DllImport("setupapi.dll",SetLastError=true)]private static extern nint SetupDiCreateDeviceInfoList(ref Guid cls,nint parent);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiOpenDeviceInfo(nint set,string id,nint parent,uint flags,ref DeviceInfo data);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiSetClassInstallParams(nint set,ref DeviceInfo data,ref PropertyChangeParameters parameters,uint size);
 [DllImport("setupapi.dll",SetLastError=true)]private static extern bool SetupDiCallClassInstaller(uint function,nint set,ref DeviceInfo data);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupDiGetDeviceInstallParams(nint set,ref DeviceInfo data,ref DeviceInstallParameters parameters);
 [DllImport("setupapi.dll")]private static extern bool SetupDiDestroyDeviceInfoList(nint set);
}
