using System.ComponentModel;
using System.Windows;
using SplitDeck.Windows;

namespace SplitDeck.App;

public partial class MainWindow
{
 private void TaskbarSettingsButton_Click(object sender,RoutedEventArgs e)
 {
  if(_busy||_previewOnly)return;
  try{TaskbarSettings.Open();}
  catch(Exception error)when(error is Win32Exception or InvalidOperationException)
  {
   ErrorText.Text="Could not open Windows taskbar settings. Open Settings > Personalization > Taskbar. "+error.Message;
  }
 }

 internal void VerifyTaskbarControls()
 {
  if(!_previewOnly)throw new InvalidOperationException("Compatibility checks require a preview-only editor.");
  if(TaskbarSettingsButton.Content?.ToString()!="Open Windows taskbar settings"||TaskbarSettingsButton.IsEnabled)
   throw new InvalidOperationException("The native Settings control must be visible and disabled in an isolated preview.");
  if(!TaskbarPositionHelp.Text.Contains("Left, Top, Right or Bottom",StringComparison.Ordinal)||!TaskbarPositionHelp.Text.Contains("Windows 11",StringComparison.Ordinal))
   throw new InvalidOperationException("Taskbar instructions must cover all four positions and Windows 11 availability.");
  if(ApplyButton.IsEnabled||SetupButton.IsEnabled)throw new InvalidOperationException("Compatibility previews must not enable display-driver operations.");
 }
}
