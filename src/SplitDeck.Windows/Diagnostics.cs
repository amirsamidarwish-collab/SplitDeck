using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using SplitDeck.Core;

namespace SplitDeck.Windows;

public static class Diagnostics
{
 public static async Task RecoverPendingRestartDesktopAsync(string journalPath,string report)
 {
  string sessions=Path.GetFullPath(Path.Combine(DriverInstallation.Root,"sessions"))+Path.DirectorySeparatorChar;
  string path=Path.GetFullPath(journalPath);
  if(!path.StartsWith(sessions,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(path),".json",StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length>131072)throw new ArgumentException("A saved SplitDeck recovery journal is required.");
  using var lease=await MachineLease.AcquireAsync(TimeSpan.FromSeconds(5));
  var journal=JsonSerializer.Deserialize<RecoveryJournal>(await File.ReadAllTextAsync(path))??throw new IOException("Invalid recovery journal.");
  if(journal.Complete)throw new InvalidOperationException("This recovery is already complete.");
  try{using var helper=System.Diagnostics.Process.GetProcessById(journal.HelperId);if(!helper.HasExited&&helper.StartTime.ToUniversalTime().Ticks==journal.HelperStarted)throw new InvalidOperationException("The display helper is still running. Use Restore desktop first.");}catch(ArgumentException){}
  DriverInstallation.RecordRestartRequired("Windows previously returned 3010 while disabling a managed display. Desktop-only recovery does not finish that driver change.");
  var before=DisplayService.Snapshot();
  Native.ReleaseCursor(0);
  try{
   DisplayService.RestoreOwned(journal.Original,journal.Expected);
   var clip=journal.Clip;var bounds=new Native.Rect(clip.Left,clip.Top,clip.Right-clip.Left,clip.Bottom-clip.Top);Native.ClipCursor(ref bounds);Native.SetCursorPos(journal.CursorX,journal.CursorY);
   DriverInstallation.AtomicWrite(path,JsonSerializer.Serialize(journal with{RestartRequired=true,Complete=false}));
   await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{DesktopRestored=true,DriverRestartRequired=true,JournalComplete=false,Before=before,After=DisplayService.Snapshot(),DriverDisableAttempted=false},new JsonSerializerOptions{WriteIndented=true}));
  }catch(Exception e){await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{DesktopRestored=false,DriverRestartRequired=true,JournalComplete=false,Error=e.Message,Before=before,After=DisplayService.Snapshot(),DriverDisableAttempted=false},new JsonSerializerOptions{WriteIndented=true}));throw;}
 }
 public static async Task SetupManagedDriverAsync(string report)
 {
  if(!EditorElevation.IsElevated)throw new InvalidOperationException("Driver setup requires Windows administrator approval.");
  await using var session=new SessionController();await session.SetupDriverAsync();
  await session.RefreshDriverReadinessAsync();
  await WriteAsync(report);if(!session.IsDriverInstalled)throw new InvalidOperationException("The managed driver pair did not verify after setup.");
 }
 public static async Task WriteAsync(string path)
 {
  var monitors=new DisplayService().Detect();bool installed;string? driverError=null;try{installed=DriverInstallation.Installed;}catch(Exception e){installed=false;driverError=e.Message;}
  var outputs=new List<object>();using(var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>()){
   for(uint a=0;factory.EnumAdapters1(a,out var adapter).Success;a++)using(adapter){for(uint o=0;adapter.EnumOutputs(o,out var output).Success;o++)using(output){var d=output.Description;outputs.Add(new{Adapter=adapter.Description1.Description,d.DeviceName,AttachedToDesktop=(bool)d.AttachedToDesktop,d.DesktopCoordinates.Left,d.DesktopCoordinates.Top,d.DesktopCoordinates.Right,d.DesktopCoordinates.Bottom});}}
  }
  await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Version="0.2.5-experimental",Windows=Environment.OSVersion.VersionString,monitors,DxgiOutputs=outputs,ManagedDriverInstalled=installed,ManagedDeviceIds=DriverInstallation.OwnedDeviceIds,DriverError=driverError,MutatedDisplaySettings=false},new JsonSerializerOptions{WriteIndented=true}));
 }
 public static async Task SmokePresentationAsync(string report)
 {
  var source=new DisplayService().Detect().First(m=>!m.IsVirtual);var hostArea=source with{X=source.X+30,Y=source.Y+30,Width=700,Height=330};
  var a=new VirtualScreenLayout(0,"Capture A",new PixelRect(5,20,340,144),source.Width,source.Height,30);
  var b=new VirtualScreenLayout(1,"Capture B",new PixelRect(355,166,340,144),source.Width,source.Height,30);
  string renderedFrame=report+".png";var frameSaved=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int captureArmed=0,captureStarted=0;
  using var host=new PresentationHost(hostArea,[new(source,a),new(source,b)],null,false,inspectFrame:(device,context,back,frame)=>{if(Volatile.Read(ref captureArmed)==1&&Interlocked.CompareExchange(ref captureStarted,1,0)==0){try{SaveFrame(device,context,back,renderedFrame);frameSaved.TrySetResult();}catch(Exception e){frameSaved.TrySetException(e);throw;}}});await host.Ready.WaitAsync(TimeSpan.FromSeconds(20));Volatile.Write(ref captureArmed,1);await frameSaved.Task.WaitAsync(TimeSpan.FromSeconds(5));long initialFrames=Interlocked.Read(ref host.FramesPresented),steadyStarted=System.Diagnostics.Stopwatch.GetTimestamp();await Task.Delay(2000);double steadySeconds=System.Diagnostics.Stopwatch.GetElapsedTime(steadyStarted).TotalSeconds;if(host.Failure is not null)throw host.Failure;
  long frames=Interlocked.Read(ref host.FramesPresented);if(frames<2)throw new InvalidOperationException("Presentation did not deliver frames.");
  await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{Result="PASS",FramesPresented=frames,SteadyFrames=frames-initialFrames,SteadySeconds=steadySeconds,SteadyPresentationFps=(frames-initialFrames)/steadySeconds,RequestedPresentationHz=30,RenderedFrame=renderedFrame,Source=source.DeviceName,PresentationSurfaces=2,UniquePhysicalCaptureStreams=1,host.GpuCaptureStreams,host.CpuFallbackCaptureStreams,host.CpuReadbackBytes,host.CaptureSummary,host.CursorDiagnostic,NativeD3D11Compositor=true,MutatedDisplaySettings=false,VirtualDriverTested=false},new JsonSerializerOptions{WriteIndented=true}));
 }
 private static void SaveFrame(ID3D11Device device,ID3D11DeviceContext context,ID3D11Texture2D back,string path)
 {
  var desc=back.Description;desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CPUAccessFlags=CpuAccessFlags.Read;desc.MiscFlags=ResourceOptionFlags.None;
  using var staging=device.CreateTexture2D(desc);context.CopyResource(staging,back);
  var mapped=context.Map(staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
  try{
   using var bitmap=new Bitmap((int)desc.Width,(int)desc.Height,PixelFormat.Format32bppArgb);
   var pixels=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
   try{var row=new byte[bitmap.Width*4];for(int y=0;y<bitmap.Height;y++){Marshal.Copy(mapped.DataPointer+(int)(y*mapped.RowPitch),row,0,row.Length);Marshal.Copy(row,0,pixels.Scan0+y*pixels.Stride,row.Length);}}
   finally{bitmap.UnlockBits(pixels);}
   bitmap.Save(path,ImageFormat.Png);
  }finally{context.Unmap(staging,0);}
 }
}
