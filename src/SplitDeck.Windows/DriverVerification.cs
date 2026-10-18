using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SplitDeck.Windows;

internal sealed record DriverHealthState(bool PackageVerified,int? ProblemCode,string Detail)
{
 internal bool Ready=>PackageVerified&&ProblemCode is 0 or 22;
 internal bool RestartRequired=>PackageVerified&&ProblemCode==14;
}

internal static class DriverVerification
{
 private static readonly IReadOnlyDictionary<string,string> Hashes=new Dictionary<string,string>{
  {"MttVDD.inf","550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D"},
  {"MttVDD.dll","C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05"},
  {"mttvdd.cat","08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6"}};
 internal static void VerifyPackage(string folder,string infName="MttVDD.inf")
 {
  if(string.IsNullOrWhiteSpace(infName)||Path.GetFileName(infName)!=infName)throw new ArgumentException("Invalid driver INF name.");
  foreach(var(file,hash)in Hashes){string path=Path.Combine(folder,file=="MttVDD.inf"?infName:file);
   if(!File.Exists(path)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))!=hash)throw new InvalidOperationException($"Driver package integrity check failed: {file}.");
  }
  foreach(string file in new[]{"MttVDD.dll","mttvdd.cat"})if(!IsTrusted(Path.Combine(folder,file)))throw new InvalidOperationException($"Windows does not trust the driver signature: {file}.");
 }
 internal static bool IsTrusted(string path)
 {
  var file=new TrustFile{Size=(uint)Marshal.SizeOf<TrustFile>(),Path=Path.GetFullPath(path)};
  nint memory=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
  Marshal.StructureToPtr(file,memory,false);
  var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),UiChoice=2,UnionChoice=1,File=memory,StateAction=1};
  var action=new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
  try{return WinVerifyTrust(-1,ref action,ref data)==0;}
  finally{data.StateAction=2;WinVerifyTrust(-1,ref action,ref data);Marshal.DestroyStructure<TrustFile>(memory);Marshal.FreeHGlobal(memory);}
 }
 internal static string DriverStoreFolder(string publishedInf)
 {
  if(string.IsNullOrWhiteSpace(publishedInf)||Path.GetFileName(publishedInf)!=publishedInf||!publishedInf.EndsWith(".inf",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid installed driver INF path.");
  var path=new StringBuilder(32768);string inf=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"INF",publishedInf);
  if(!SetupGetInfDriverStoreLocation(inf,0,null,path,(uint)path.Capacity,out _))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot locate the installed driver package.");
  return Path.GetDirectoryName(path.ToString())??throw new InvalidOperationException("Missing installed driver folder.");
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct TrustFile{public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Path;public nint File;public nint KnownSubject;}
 [StructLayout(LayoutKind.Sequential)]private struct TrustData{public uint Size;public nint PolicyCallback;public nint SipClient;public uint UiChoice;public uint RevocationChecks;public uint UnionChoice;public nint File;public uint StateAction;public nint State;public nint Url;public uint ProviderFlags;public uint UiContext;public nint SignatureSettings;}
 [DllImport("wintrust.dll",ExactSpelling=true)]private static extern int WinVerifyTrust(nint window,ref Guid action,ref TrustData data);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetupGetInfDriverStoreLocation(string file,nint platform,string? locale,StringBuilder buffer,uint size,out uint required);
}
