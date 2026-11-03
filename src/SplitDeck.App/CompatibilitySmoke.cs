using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SplitDeck.Core;
using SplitDeck.Windows;

namespace SplitDeck.App;

internal static class CompatibilitySmoke
{
 internal static int Run(string report)
 {
  var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
  var window=new MainWindow(Presets.Balanced(60)){ShowActivated=false,ShowInTaskbar=false};
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
  timer.Tick+=(_,_)=>{
   timer.Stop();
   try{
    window.UpdateLayout();window.VerifyTaskbarControls();
    var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(report+".png"))encoder.Save(stream);
    File.WriteAllText(report,JsonSerializer.Serialize(new{Result="PASS",Version=typeof(Program).Assembly.GetName().Version?.ToString(),Windows=RuntimeInformation.OSDescription,OSArchitecture=RuntimeInformation.OSArchitecture.ToString(),ProcessArchitecture=RuntimeInformation.ProcessArchitecture.ToString(),Framework=RuntimeInformation.FrameworkDescription,TaskbarSettingsUri=TaskbarSettings.SettingsUri,TaskbarControlsVerified=true,PreviewOnly=true,DriverActionsDisabled=true,WindowsSettingsOpened=false,MutatedDisplaySettings=false,Scope="Preview editor startup and rendering only. No driver installation, active split, GPU performance or taskbar movement was tested."},new JsonSerializerOptions{WriteIndented=true}));
    window.Close();
   }catch(Exception error){File.WriteAllText(report,JsonSerializer.Serialize(new{Result="FAIL",Error=error.ToString(),MutatedDisplaySettings=false},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(1);}
  };
  window.Loaded+=(_,_)=>timer.Start();
  return app.Run(window);
 }
}
