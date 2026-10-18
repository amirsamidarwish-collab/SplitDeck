using SplitDeck.Core;
using SplitDeck.Windows;
using System.Text.Json;

internal static class DriverCountTests
{
 internal static Task Ownership()
 {
  string[] ids=["ROOT\\DISPLAY\\0000","ROOT\\DISPLAY\\0001","ROOT\\DISPLAY\\0002"];
  string Owner(params string[] extra)=>JsonSerializer.Serialize(new{DeviceId=ids[0],Version="25.7.23",AdditionalDeviceIds=extra});
  Check(DriverInstallation.ParseOwnedDevices(Owner(ids[1],ids[2])).SequenceEqual(ids),"A third owned instance must survive serialization.");
  Check(DriverInstallation.ExclusivelyOwned(ids,ids.Reverse().ToArray()),"An exclusively owned triple must be accepted.");
  Check(DriverInstallation.ParseOwnedDevices(Owner(ids[1],ids[2],"ROOT\\DISPLAY\\0003")).Length==0,"More than three owned devices must fail closed.");
  Check(!DriverInstallation.ExclusivelyOwned(ids,[..ids,"ROOT\\DISPLAY\\0003"]),"A foreign fourth device must be refused.");
  Check(DriverInstallation.ParseOwnedDevices(Owner(ids[1],ids[1])).Length==0,"Duplicate instance IDs must be refused.");
  return Task.CompletedTask;
 }
 internal static Task ModesAndAttachment()
 {
  VirtualScreenLayout[] layouts=[new(0,"Left",new(0,0,320,1080),320,1080,30),new(1,"Center",new(320,0,1920,1080),1920,1080,60),new(2,"Right",new(2240,0,320,1080),320,1080,30)];
  var xml=DriverConfiguration.Build(layouts);Check(xml.Descendants("count").Single().Value=="1","Each instance must still expose only one output.");
  Check(xml.Descendants("resolution").Count()==2,"Duplicate wing modes must not duplicate the XML mode list.");
  string[] keys=["left","center","right"];var adapters=keys.Select((key,i)=>new DisplayAdapterState("DISPLAY"+i,"Root\\MttVDD",key,false)).ToArray();
  var anchor=new DisplaySnapshot("physical","gpu","physical-key",0,0,2560,1080,144,true);
  var plan=ManagedOutputAttachment.Plan(adapters,keys,layouts,anchor);
  Check(plan.Count==3&&plan.Select(x=>x.Width).SequenceEqual(new[]{320,1920,320}),"All three owned outputs need their corresponding modes.");
  Check(plan[1].X==2880&&plan[2].X==4800,"Detached output placement must stay contiguous beside the visible anchor.");
  Check(ManagedOutputAttachment.Plan(adapters,["left","center","foreign"],layouts,anchor).Count==0,"Never guess the third output identity.");
  return Task.CompletedTask;
 }
 internal static async Task Upgrade()
 {
  string[] owner=["first","second"];var disabled=new List<string>();var removed=new List<string>();int binds=0;
  await DriverPairUpgrade.RunAsync(owner,3,()=>"third",ids=>owner=ids,()=>{binds++;Check(owner.SequenceEqual(new[]{"first","second","third"}),"Persist new ownership before binding the package.");return Task.CompletedTask;},Disable,Remove);
  Check(binds==1&&removed.Count==0&&disabled.Count==3,"Growing a pair must disable every retained device and remove none.");
  await DriverPairUpgrade.RunAsync(owner,2,()=>throw new Exception("Do not create on reduction."),ids=>owner=ids,()=>throw new Exception("Do not bind on reduction."),Disable,Remove);
  Check(owner.Length==3&&removed.Count==0,"A two-screen profile must retain third-device ownership.");
  Task Disable(string id){disabled.Add(id);return Task.CompletedTask;}Task Remove(string id){removed.Add(id);return Task.CompletedTask;}
 }
 internal static async Task UpgradeRollback()
 {
  foreach(bool failRemoval in new[]{false,true}){
   string[] owner=["first"];int created=0;var removed=new List<string>();var disabled=new List<string>();
   try{
    await DriverPairUpgrade.RunAsync(owner,3,()=>++created==1?"second":"third",ids=>owner=ids,()=>created==2?Task.FromException(new IOException("Third binding failed.")):Task.CompletedTask,id=>{disabled.Add(id);return Task.CompletedTask;},id=>{removed.Add(id);return failRemoval&&id=="third"?Task.FromException(new IOException("Removal failed.")):Task.CompletedTask;});
    throw new Exception("Expected upgrade failure.");
   }catch(Exception e)when(e is IOException or AggregateException){}
   Check(removed.SequenceEqual(new[]{"third","second"}),"Rollback removes newly created devices in reverse order, never the legacy owner.");
   Check(owner.SequenceEqual(failRemoval?new[]{"first","third"}:new[]{"first"}),"Failed removal must retain exactly the IDs that may still exist.");
   Check(owner.All(disabled.Contains),"Rollback must attempt to disable every retained instance.");
  }
 }
 internal static async Task ActivationSelection()
 {
  var calls=new List<string>();Task Change(string id,bool enable){calls.Add((enable?"enable ":"disable ")+id);return Task.CompletedTask;}
  await DriverActivation.RunAsync(["first","second","third"],2,Change);
  Check(calls.SequenceEqual(new[]{"disable third","enable first","enable second"}),"Two-screen activation must disable the unused owned instance before enabling the requested pair.");
  calls.Clear();await DriverActivation.RunAsync(["first","second","third"],3,Change);
  Check(calls.SequenceEqual(new[]{"enable first","enable second","enable third"}),"Triple activation must enable exactly the requested three.");
  calls.Clear();try{await DriverActivation.RunAsync(["first","second"],3,Change);throw new Exception("Expected insufficient ownership.");}catch(ArgumentException){}
  Check(calls.Count==0,"Invalid counts must not mutate any device.");
 }
 internal static Task PrimaryAndLegacyRequest()
 {
  VirtualScreenLayout[] layouts=[new(0,"Left",new(0,0,320,1080),320,1080,30),new(1,"Center",new(320,0,1920,1080),1920,1080,60),new(2,"Right",new(2240,0,320,1080),320,1080,30)];
  MonitorInfo[] monitors=Enumerable.Range(0,3).Select(i=>new MonitorInfo("DISPLAY"+i,"Virtual",0,0,100,100,60,null,false,true)).ToArray();
  var modes=SessionDisplayPlan.Build(monitors,layouts,1);
  Check(modes.Select(m=>m.X).SequenceEqual(new[]{-320,0,1920}),"Logical desktop origin must follow the selected center primary without changing screen order.");
  Check(modes.Single(m=>m.Primary).DeviceName=="DISPLAY1","The explicit primary is independent of left-to-right screen order.");
  var pair=SessionDisplayPlan.Build(monitors.Take(2).ToArray(),layouts.Take(2).ToArray(),0);
  Check(pair[0].X==0&&pair[1].X==320&&pair[0].Primary,"Legacy two-screen placement must remain unchanged.");
  var old=JsonSerializer.Deserialize<HelperRequest>("{\"Mode\":\"install\",\"ParentId\":1,\"ParentStarted\":1,\"Pipe\":\"old\",\"Token\":\"old\"}")!;
  using var json=JsonDocument.Parse(JsonSerializer.Serialize(old));Check(json.RootElement.TryGetProperty("ScreenCount",out var count)&&count.GetInt32()==2,"Legacy helper requests must default to two screens.");
  return Task.CompletedTask;
 }
 private static void Check(bool value,string detail){if(!value)throw new Exception(detail);}
}
