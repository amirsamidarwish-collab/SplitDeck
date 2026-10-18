using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SplitDeck.Windows;
using SplitDeck.Core;
using System.Text.Json;

namespace SplitDeck.App;

public static class Program
{
 [STAThread]
 public static int Main(string[] args)
 {
  try{
   if(HelperRuntime.IsHelperCommand(args))return HelperRuntime.RunAsync(args).GetAwaiter().GetResult();
   if(args.Length==3&&args[0]=="--recover-pending-restart-desktop"){Diagnostics.RecoverPendingRestartDesktopAsync(args[1],args[2]).GetAwaiter().GetResult();return 0;}
   if(args.Length==2&&args[0]=="--diagnose"){Diagnostics.WriteAsync(args[1]).GetAwaiter().GetResult();return 0;}
   if(args.Length==2&&args[0]=="--setup-driver"){Diagnostics.SetupManagedDriverAsync(args[1]).GetAwaiter().GetResult();return 0;}
   if(args.Length==2&&args[0]=="--presentation-smoke"){Diagnostics.SmokePresentationAsync(args[1]).GetAwaiter().GetResult();return 0;}
   if(args.Length==2&&args[0]=="--session-benchmark"){PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1]).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   if(args.Length==2&&args[0]=="--interaction-benchmark"){PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1],true).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   if(args.Length==2&&args[0]=="--interaction-benchmark-60"){PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1],true,60).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   if(args.Length==2&&args[0] is "--desktop-menu-trial" or "--desktop-menu-trial-immediate"){if(args[0]=="--desktop-menu-trial-immediate")Environment.SetEnvironmentVariable("SPLITDECK_DIAGNOSTIC_IMMEDIATE","1");PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1],desktopMenuTrial:true).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   if(args.Length==2&&args[0]=="--three-screen-trial"){PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1],desktopMenuTrial:true,threeScreenTrial:true).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   if(args.Length==3&&args[0]=="--profile-trial"){var profile=JsonSerializer.Deserialize<LayoutProfile>(File.ReadAllText(args[2]))??throw new ArgumentException("A valid diagnostic layout profile is required.");PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1],desktopMenuTrial:true,diagnosticProfile:profile).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   if(args.Length==2&&args[0]=="--interaction-diagnostic"){PerformanceProbe.KeepDisplayAwake(true);try{return PerformanceProbe.RunAsync(args[1],interactionWorkload:true,desktopMenuTrial:true).GetAwaiter().GetResult();}finally{PerformanceProbe.KeepDisplayAwake(false);}}
   EditorLaunchRequest? elevatedRequest=args.Length==2&&args[0]=="--elevated-editor"?EditorElevation.Decode(args[1]):null;
   bool editorInputSmoke=args.Length==2&&args[0]=="--editor-input-smoke";
   var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var window=editorInputSmoke||args.Length==2&&args[0]=="--ui-smoke-three"?new MainWindow(Presets.Triple(60)):new MainWindow();
   if(editorInputSmoke){var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};timer.Tick+=(_,_)=>{timer.Stop();try{var result=window.VerifyEditorInputs();File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));window.Close();}catch(Exception e){File.WriteAllText(args[1],JsonSerializer.Serialize(new{Result="FAIL",Error=e.ToString(),MutatedDisplaySettings=false},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(1);}};window.Loaded+=(_,_)=>timer.Start();}
   if(elevatedRequest is not null)window.Loaded+=(_,_)=>{_=window.Dispatcher.BeginInvoke(new Action(()=>window.ResumeElevatedRequest(elevatedRequest)));};
   if(args.Length==2&&args[0] is "--ui-smoke" or "--ui-smoke-three"){
    var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=(_,_)=>{timer.Stop();try{window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(args[1]))encoder.Save(stream);window.Close();}catch(Exception e){File.WriteAllText(args[1]+".error.txt",e.ToString());app.Shutdown(1);}};window.Loaded+=(_,_)=>timer.Start();
   }
   app.DispatcherUnhandledException+=(_,e)=>{MessageBox.Show(e.Exception.Message,"SplitDeck",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
   return app.Run(window);
  }catch(Exception e){if(args.Length>0){try{string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SplitDeck");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"last-error.txt"),e.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}}if(args.Length==0||args[0]=="--elevated-editor")MessageBox.Show(e.Message,"SplitDeck",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
 }
}
