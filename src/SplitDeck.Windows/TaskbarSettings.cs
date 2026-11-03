using System.Diagnostics;

namespace SplitDeck.Windows;

public static class TaskbarSettings
{
 public const string SettingsUri="ms-settings:taskbar";

 public static ProcessStartInfo CreateStartInfo()=>new(SettingsUri)
 {
  UseShellExecute=true,
  WindowStyle=ProcessWindowStyle.Normal
 };

 public static void Open()
 {

  using var process=Process.Start(CreateStartInfo());
 }
}
