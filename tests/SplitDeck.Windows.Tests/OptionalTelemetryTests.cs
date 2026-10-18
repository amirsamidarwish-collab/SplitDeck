using SplitDeck.Windows;
internal static class OptionalTelemetryTests
{
 internal static async Task Sharing(){string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{await File.WriteAllTextAsync(path,"{\"HelperId\":123}");using(var held=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){if(await OptionalTelemetry.ReadAsync(path) is not null)throw new Exception("Locked telemetry should be unavailable.");}var valid=await OptionalTelemetry.ReadAsync(path);if(valid?.GetProperty("HelperId").GetInt32()!=123)throw new Exception("A transient lock must not poison the next sample.");await File.WriteAllTextAsync(path,"{");if(await OptionalTelemetry.ReadAsync(path) is not null)throw new Exception("Partial telemetry is unavailable.");}finally{File.Delete(path);}}
}
