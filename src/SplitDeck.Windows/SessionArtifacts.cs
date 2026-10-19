using System.Text.Json;

namespace SplitDeck.Windows;

public static class SessionArtifacts
{
 // Settings stay in memory in the editor. Clear only this application's known
 // local transient files after confirmed recovery; never touch other app data.
 public static void ClearLocalTransientData()
 {
  try{
  string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SplitDeck");
  if(!Directory.Exists(folder)||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)return;
  foreach(string name in new[]{"profile.json","last-error.txt","operations.log"})TryDelete(Path.Combine(folder,name));
  foreach(string path in Directory.EnumerateFiles(folder,"profile.*.tmp",SearchOption.TopDirectoryOnly)){
   string token=Path.GetFileName(path)[8..^4];if(Guid.TryParseExact(token,"N",out _))TryDelete(path);
  }
  }catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
 }

 internal static void PruneCompleted(string path)
 {
  string allowed=Path.GetFullPath(Path.Combine(DriverInstallation.Root,"sessions"))+Path.DirectorySeparatorChar;
  string full=Path.GetFullPath(path);
  if(!string.Equals(Path.GetDirectoryName(full)+Path.DirectorySeparatorChar,allowed,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileNameWithoutExtension(full),"N",out _))return;
  try{
   var journal=JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(full));
   if(journal is null||!journal.Complete||journal.RetainDiagnostics)return;
   // The watcher calls this only after the helper exits, under the machine lease.
   foreach(string suffix in new[]{".ready",".heartbeat",".performance",".topology",".display-adapters",".window-move",".window-restore",".error.txt"})TryDelete(full+suffix);
   TryDelete(full);
  }catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){}
 }
 private static void TryDelete(string path){try{File.Delete(path);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}}
}
