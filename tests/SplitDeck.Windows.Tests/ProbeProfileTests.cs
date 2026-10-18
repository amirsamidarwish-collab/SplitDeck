using SplitDeck.Core;
using SplitDeck.Windows;
internal static class ProbeProfileTests
{
 internal static Task Selection()
 {
  var saved=Presets.Gaming(144,true);var custom=Presets.Triple(60) with{PrimaryIndex=2};
  Check(PerformanceProbe.SelectProfile(saved,false,null,null)==saved,"Ordinary trials must retain the saved profile.");
  var triple=PerformanceProbe.SelectProfile(saved,true,null,null);Check(triple.Screens.Count==3&&triple.PrimaryIndex==1,"Explicit triple trial must select center primary without overwriting the saved profile.");
  var overridden=PerformanceProbe.SelectProfile(saved,false,null,custom);Check(overridden==custom,"An explicit diagnostic profile takes precedence over saved settings.");
  var capped=PerformanceProbe.SelectProfile(saved,false,60,custom);Check(capped.Screens.All(s=>s.RefreshHz==60),"A diagnostic refresh override must include Third.");
  Check(saved.First.RefreshHz==144&&custom.First.RefreshHz==30,"Diagnostic selection must not mutate either input profile.");
  return Task.CompletedTask;
 }
 static void Check(bool value,string detail){if(!value)throw new Exception(detail);}
}
