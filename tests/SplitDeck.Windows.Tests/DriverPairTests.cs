using SplitDeck.Windows;
internal static class DriverPairTests
{
 internal static Task Ownership()
 {
  string first="ROOT\\DISPLAY\\0000",second="ROOT\\DISPLAY\\0001";
  string Json(string[]? extras=null)=>System.Text.Json.JsonSerializer.Serialize(new{DeviceId=first,Version="25.7.23",AdditionalDeviceIds=extras});
  Check(DriverInstallation.ParseOwnedDevices(Json()).SequenceEqual(new[]{first}));
  Check(DriverInstallation.ParseOwnedDevices(Json([second])).SequenceEqual(new[]{first,second}));
  Check(DriverInstallation.ParseOwnedDevices(Json([first])).Length==0);
  Check(DriverInstallation.ParseOwnedDevices(Json([second,"ROOT\\DISPLAY\\0002"])).Length==3);
  Check(DriverInstallation.ParseOwnedDevices(Json([second,"ROOT\\DISPLAY\\0002","ROOT\\DISPLAY\\0003"])).Length==0);
  Check(DriverInstallation.ParseOwnedDevices(Json(["PCI\\foreign"])).Length==0);
  Check(DriverInstallation.ParseOwnedDevices("{}").Length==0);
  Check(DriverInstallation.ParseOwnedDevices("not json").Length==0);
  Check(DriverInstallation.ExclusivelyOwned([first,second],[second,first]));
  Check(!DriverInstallation.ExclusivelyOwned([first],[first,second]));
  return Task.CompletedTask;
 }
 internal static async Task Upgrade()
 {
  string[] owner=["first"];var removed=new List<string>();var disabled=new List<string>();
  await DriverPairUpgrade.RunAsync("first",()=>"second",ids=>owner=ids,()=>{Check(owner.SequenceEqual(new[]{"first","second"}));return Task.CompletedTask;},id=>{disabled.Add(id);return Task.CompletedTask;},id=>{removed.Add(id);return Task.CompletedTask;});
  Check(owner.Length==2&&removed.Count==0&&disabled.SequenceEqual(new[]{"first","second"}));
  foreach(bool failRemoval in new[]{false,true}){
   owner=["first"];removed.Clear();disabled.Clear();
   try{await DriverPairUpgrade.RunAsync("first",()=>"second",ids=>owner=ids,()=>throw new IOException("bind failed"),id=>{disabled.Add(id);return Task.CompletedTask;},id=>{removed.Add(id);return failRemoval?Task.FromException(new IOException("remove failed")):Task.CompletedTask;});throw new Exception("Expected failed upgrade.");}
   catch(Exception e)when(e is IOException or AggregateException){}
   Check(removed.SequenceEqual(new[]{"second"})&&disabled.Contains("first"));
   Check(owner.SequenceEqual(failRemoval?new[]{"first","second"}:new[]{"first"}));
   if(failRemoval)Check(disabled.Contains("second"));
  }
 }
 static void Check(bool value){if(!value)throw new Exception("Driver pair ownership/rollback failed.");}
}
