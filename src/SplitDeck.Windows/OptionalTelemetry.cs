using System.Text.Json;
namespace SplitDeck.Windows;
internal static class OptionalTelemetry
{
 internal static async Task<JsonElement?> ReadAsync(string path)
 {
  try{
   await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,FileOptions.Asynchronous);
   using var json=await JsonDocument.ParseAsync(stream);
   return json.RootElement.Clone();
  }catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){return null;}
 }
}
