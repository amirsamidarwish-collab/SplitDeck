using System.Diagnostics;
using System.Reflection;
using SplitDeck.Windows;

internal static class TaskbarSettingsTests
{
 internal static Task NativeSettingsBoundary()
 {
  var type=typeof(DisplayService).Assembly.GetType("SplitDeck.Windows.TaskbarSettings");
  if(type is null)throw new Exception("A dedicated native taskbar settings launcher is missing.");
  var method=type.GetMethod("CreateStartInfo",BindingFlags.Public|BindingFlags.Static)??throw new Exception("The native launch request must be inspectable without launching Settings.");
  var request=(ProcessStartInfo)method.Invoke(null,null)!;
  if(request.FileName!="ms-settings:taskbar"||!request.UseShellExecute)throw new Exception("Open Windows' documented native taskbar URI through the shell.");
  if(request.ArgumentList.Count!=0||request.Arguments.Length!=0||request.Verb.Length!=0||request.UserName.Length!=0)throw new Exception("Taskbar settings must not use commands, arguments, another account or elevation.");
  if(request.WindowStyle!=ProcessWindowStyle.Normal)throw new Exception("Windows Settings must be visible for the user's choice.");
  return Task.CompletedTask;
 }
}
