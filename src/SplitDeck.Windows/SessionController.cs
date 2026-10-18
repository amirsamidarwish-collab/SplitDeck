using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SplitDeck.Core;

namespace SplitDeck.Windows;

internal sealed record HelperRequest(string Mode,int ParentId,long ParentStarted,string Pipe,string Token,string? Monitor,LayoutProfile? Profile,int ScreenCount=2);
internal sealed record HelperMessage(string Type,string Text);
public sealed class SessionController:IAsyncDisposable
{
 public event Action<string>? StatusChanged;public event Action? SessionEnded;
 internal int? HelperId=>current?.Helper?.Id;
 internal async Task WaitForHelperExitAsync(TimeSpan timeout){if(current?.Helper is { } helper)await helper.WaitForExitAsync().WaitAsync(timeout);}
 public bool IsActive{get;private set;}public string CaptureSummary{get;private set;}="";
 private readonly DriverReadinessCache readiness;private int readinessCount=2;
 // Rendering reads this snapshot only; trust, file and device checks run on the pool.
 public bool IsDriverInstalled=>readiness.State.Ready;
 public bool IsDriverReadinessKnown=>readiness.State.Known;
 public bool IsCheckingDriverReadiness=>readiness.State.Checking;
 public string? DriverReadinessError=>readiness.State.Error;
 public Task RefreshDriverReadinessAsync()=>readiness.RefreshAsync();
 public Task RefreshDriverReadinessAsync(int screenCount){if(screenCount is not(2 or 3))throw new ArgumentOutOfRangeException(nameof(screenCount));if(Interlocked.Exchange(ref readinessCount,screenCount)!=screenCount)readiness.Invalidate();return readiness.RefreshAsync();}
 private sealed class Attempt:IDisposable
 {
  internal required NamedPipeServerStream Pipe;internal StreamWriter? Writer;internal Process? Helper;internal readonly TaskCompletionSource End=new(TaskCreationOptions.RunContinuationsAsynchronously);internal Exception? Error;internal bool Restored;internal Task Reader=Task.CompletedTask;internal readonly CancellationTokenSource Cancel=new();
  public void Dispose(){try{Writer?.Dispose();}catch(Exception e)when(e is IOException or ObjectDisposedException){}Pipe.Dispose();Helper?.Dispose();Cancel.Dispose();}
 }
 private readonly Func<IReadOnlyList<string>,bool,Process> launch;private readonly Action accountCheck;private readonly TimeSpan startupTimeout;
 public SessionController():this(StartChild,RequireSupportedAccount,TimeSpan.FromSeconds(90)){}
 internal SessionController(Func<IReadOnlyList<string>,bool,Process> launch,Action accountCheck,TimeSpan timeout){this.launch=launch;this.accountCheck=accountCheck;startupTimeout=timeout;readiness=new(()=>DriverInstallation.InstalledForCount(Volatile.Read(ref readinessCount)));}
 private Attempt? current;private readonly SemaphoreSlim gate=new(1,1);private readonly SemaphoreSlim writes=new(1,1);private bool disposed;
 public Task SetupDriverAsync(CancellationToken ct=default)=>StartAsync("install",null,null,ct);
 public Task SetupDriverAsync(int screenCount,CancellationToken ct=default){if(screenCount is not(2 or 3))throw new ArgumentOutOfRangeException(nameof(screenCount));return StartAsync("install",null,null,ct,screenCount);}
 public Task ApplyAsync(MonitorInfo monitor,LayoutProfile profile,CancellationToken ct=default){LayoutEngine.Calculate(profile,monitor.Width,monitor.Height,monitor.RefreshHz);return StartAsync("apply",monitor,profile,ct);}
 private static void RequireSupportedAccount()
 {
  using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();
  var admin=new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid,null);
  if(identity.Groups?.Contains(admin)!=true)throw new InvalidOperationException("This experimental release requires signing into Windows with an administrator account. Standard-user elevation using another account is not supported. Switch Windows accounts before Setup or Apply.");
 }
 private async Task StartAsync(string mode,MonitorInfo? monitor,LayoutProfile? profile,CancellationToken ct,int screenCount=2)
 {
  await gate.WaitAsync(ct);try{
   ObjectDisposedException.ThrowIf(disposed,this);accountCheck();CaptureSummary="";
   if(current is { } previous){if(!previous.End.Task.IsCompleted)throw new InvalidOperationException("Restore the current session first.");await previous.Reader;previous.Dispose();current=null;}
   if(mode=="apply"){
    // Always refresh at the operation boundary; a cached editor result cannot
    // authorize activation. The elevated helper validates again independently.
    await RefreshDriverReadinessAsync(profile!.Screens.Count).WaitAsync(ct);
    if(!IsDriverInstalled)throw new InvalidOperationException("Set up the SplitDeck driver before applying a layout."+(DriverReadinessError is { } error?" "+error:""));
   }else readiness.Invalidate();
   string id=Guid.NewGuid().ToString("N"),pipeName="SplitDeck-"+id,token=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
   var attempt=new Attempt{Pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly)};current=attempt;
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SplitDeck","requests");string path=Path.Combine(folder,id+".json");
   var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   try{
    Directory.CreateDirectory(folder);using var parent=Process.GetCurrentProcess();var request=new HelperRequest(mode,parent.Id,parent.StartTime.ToUniversalTime().Ticks,pipeName,token,monitor?.DeviceName,profile,screenCount);
    await File.WriteAllTextAsync(path,JsonSerializer.Serialize(request),ct);
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Cancel.Token);timeout.CancelAfter(startupTimeout);
    StatusChanged?.Invoke("Waiting for Windows approval...");attempt.Helper=launch(["--helper",path],true);
    await attempt.Pipe.WaitForConnectionAsync(timeout.Token);
    var reader=new StreamReader(attempt.Pipe,Encoding.UTF8,false,4096,true);attempt.Writer=new StreamWriter(attempt.Pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
    string? hello=await reader.ReadLineAsync(timeout.Token);if(hello!=token){reader.Dispose();throw new IOException("Display helper authentication failed.");}
    attempt.Reader=ReadMessagesAsync(attempt,reader,ready,path);await ready.Task.WaitAsync(timeout.Token);
   }catch{
    attempt.Cancel.Cancel();attempt.Pipe.Dispose();await attempt.Reader;attempt.End.TrySetResult();try{File.Delete(path);}catch(Exception e)when(e is IOException or ObjectDisposedException){}throw;
   }
  }finally{gate.Release();}
 }
 private async Task ReadMessagesAsync(Attempt attempt,StreamReader reader,TaskCompletionSource ready,string requestPath)
 {
  Exception? error=null;bool finished=false;try{string? line;while((line=await reader.ReadLineAsync(attempt.Cancel.Token))is not null){var message=JsonSerializer.Deserialize<HelperMessage>(line)??throw new IOException("Invalid display helper reply.");StatusChanged?.Invoke(message.Text);switch(message.Type){case "ACTIVE":CaptureSummary=message.Text;IsActive=true;ready.TrySetResult();break;case "COMPLETE":finished=true;ready.TrySetResult();break;case "ERROR":error=new InvalidOperationException(message.Text);ready.TrySetException(error);break;case "RESTORED":finished=true;attempt.Restored=true;IsActive=false;break;}}
   if(!finished){error??=new IOException("Display helper disconnected without confirming recovery.");ready.TrySetException(error);}
  }catch(Exception e){error=e;ready.TrySetException(e);StatusChanged?.Invoke(e.Message);}finally{IsActive=false;readiness.Invalidate();reader.Dispose();try{attempt.Writer?.Dispose();}catch(Exception e)when(e is IOException or ObjectDisposedException){}attempt.Writer=null;attempt.Pipe.Dispose();try{File.Delete(requestPath);}catch(Exception e)when(e is IOException or ObjectDisposedException){}attempt.Error=error;attempt.End.TrySetResult();if(error is not null)StatusChanged?.Invoke("Session ended with an error: "+error.Message);SessionEnded?.Invoke();}
 }
 private async Task SendAsync(Attempt? attempt,string command){await writes.WaitAsync();try{if(attempt?.Writer is { } writer)await writer.WriteLineAsync(command);}catch(Exception e)when(e is IOException or ObjectDisposedException){StatusChanged?.Invoke(e.Message);}finally{writes.Release();}}
 public async Task RestoreAsync(){var attempt=current;if(attempt is null)return;if(attempt.End.Task.IsCompleted){if(attempt.Error is not null&&!attempt.Restored)throw new InvalidOperationException("Recovery was not confirmed.",attempt.Error);return;}if(attempt.Writer is null)attempt.Cancel.Cancel();else await SendAsync(attempt,"STOP");await attempt.End.Task.WaitAsync(TimeSpan.FromSeconds(30));await attempt.Reader;if(attempt.Error is not null&&!attempt.Restored)throw new InvalidOperationException("Recovery was not confirmed.",attempt.Error);}
 public async ValueTask DisposeAsync()
 {
  if(current?.Writer is null)current?.Cancel.Cancel();await gate.WaitAsync();try{
   // A completed helper's operation error remains visible, but cannot prevent app close.
   // A timeout with a live helper still aborts disposal and permits a later retry.
   try{await RestoreAsync();}catch(InvalidOperationException)when(current?.End.Task.IsCompleted==true){}
   if(current is { } attempt){await attempt.Reader;attempt.Dispose();current=null;}
   disposed=true;
  }finally{gate.Release();}
 }
 internal static Process StartChild(IReadOnlyList<string> args,bool elevate)
 {
  string exe=Environment.ProcessPath??throw new IOException("Cannot locate SplitDeck executable.");var start=new ProcessStartInfo(exe){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden};if(elevate)start.Verb="runas";
  if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
  foreach(string a in args)start.ArgumentList.Add(a);return Process.Start(start)??throw new IOException("Could not start display helper.");
 }
}

