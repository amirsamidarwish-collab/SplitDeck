using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SplitDeck.Core;

namespace SplitDeck.Windows;

public sealed record EditorLaunchRequest(LayoutProfile Profile,string? MonitorDeviceName,string Action);

public static class EditorElevation
{
 public static bool IsElevated
 {
  get{using var identity=WindowsIdentity.GetCurrent();return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);}
 }
 public static ProcessStartInfo CreateRestartInfo(string executable,string? entryAssembly,EditorLaunchRequest request)
 {
  Validate(request);ArgumentException.ThrowIfNullOrWhiteSpace(executable);
  var start=new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Normal};
  if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase)){
   ArgumentException.ThrowIfNullOrWhiteSpace(entryAssembly);start.ArgumentList.Add(entryAssembly);
  }
  string encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
  if(encoded.Length>32768)throw new ArgumentException("The editor settings are too large to transfer.");
  start.ArgumentList.Add("--elevated-editor");start.ArgumentList.Add(encoded);return start;
 }
 public static EditorLaunchRequest Decode(string encoded)
 {
  if(string.IsNullOrEmpty(encoded)||encoded.Length>32768)throw new ArgumentException("Invalid editor settings payload.");
  try{var request=JsonSerializer.Deserialize<EditorLaunchRequest>(Convert.FromBase64String(encoded))??throw new ArgumentException("Missing editor settings.");Validate(request);return request;}
  catch(Exception e)when(e is FormatException or JsonException){throw new ArgumentException("The transferred editor settings could not be read.",e);}
 }
 private static void Validate(EditorLaunchRequest request)
 {
  ArgumentNullException.ThrowIfNull(request);
  if(request.Action is not("setup" or "apply")||request.Profile is null||request.Profile.First is null||request.Profile.Second is null)throw new ArgumentException("Invalid editor operation.");
 }
 public static void Restart(EditorLaunchRequest request)
 {
  string executable=Environment.ProcessPath??throw new IOException("Could not locate SplitDeck.exe.");
  using var process=Process.Start(CreateRestartInfo(executable,Assembly.GetEntryAssembly()?.Location,request))??throw new IOException("Windows did not start the administrator copy of SplitDeck.");
 }
}
