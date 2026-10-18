using SplitDeck.Core;
using SplitDeck.Windows;
internal static class OutputRefreshTests
{
 internal static Task Bounds()
 {
  VirtualScreenLayout[] layouts=[new(0,"Left",new(0,0,1280,1080),1280,1080,60),new(1,"Right",new(1280,0,1280,1080),1280,1080,60)];
  Check(OutputRefreshPolicy.Select(0,layouts,144) is null,"Default profiles must preserve the current physical output refresh.");
  Check(OutputRefreshPolicy.Select(60,layouts,144)==60,"A diagnostic panel refresh matching the unchanged virtual rates is allowed.");
  foreach(int invalid in new[]{-1,30,145,1001})Reject(()=>OutputRefreshPolicy.Select(invalid,layouts,144));
  Reject(()=>OutputRefreshPolicy.Select(60,[layouts[0] with{RefreshHz=144},layouts[1]],144));
  Check(OutputRefreshPolicy.Select(144,layouts.Select(l=>l with{RefreshHz=144}).ToArray(),144)==144,"The lower bound uses capped actual layout rates, not uncapped user settings.");
  return Task.CompletedTask;
 }
 internal static Task IntentAndRestoration()
 {
  DisplaySnapshot[] original=[new("selected","gpu","one",0,0,2560,1080,144,true),new("other","gpu2","two",2560,0,1920,1080,75,false)];
  var intent=OutputRefreshPolicy.PhysicalModes(original,"selected",2360,60);
  Check(intent[0].Hz==60&&intent[1].Hz==75,"Only the selected physical output may get a diagnostic refresh override.");
  Check(intent.All(m=>m.Y==-2360&&!m.Primary),"Physical expected intent must match the staged hidden positions.");
  Check(original[0].Hz==144&&original[0].Y==0&&original[0].Primary,"The recovery baseline must retain the original 144 Hz mode.");
  var defaults=OutputRefreshPolicy.PhysicalModes(original,"selected",2360,null);Check(defaults[0].Hz==144&&defaults[1].Hz==75,"Default staging retains each physical display's independent rate.");
  var autoRestored=original.Select(m=>m.DeviceName=="selected"?m with{Hz=60}:m).ToArray();
  Check(OwnedOutputRecovery.Plan(original,intent,autoRestored,[]).ReplayOriginal,"Disabling the virtual primary can restore physical geometry before its diagnostic Hz is restored.");
  Check(!OwnedOutputRecovery.Plan(original,intent,[autoRestored[0] with{Hz=50},autoRestored[1]],[]).ReplayOriginal,"An unrelated refresh change must still refuse saved topology replay.");
  Check(!OwnedOutputRecovery.Plan(original,intent,[autoRestored[0] with{X=20},autoRestored[1]],[]).ReplayOriginal,"An unrelated physical position change must still refuse saved topology replay.");
  return Task.CompletedTask;
 }
 static void Reject(Action action){try{action();throw new Exception("Expected unsafe output refresh to be rejected.");}catch(ArgumentException){}}
 static void Check(bool value,string detail){if(!value)throw new Exception(detail);}
}
