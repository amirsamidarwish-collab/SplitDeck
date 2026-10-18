using SplitDeck.Windows;
using SplitDeck.Core;
internal static class OutputAttachmentTests
{
 internal static Task DetachedOwnedOutputs()
 {
  var layouts=LayoutEngine.Calculate(Presets.Gaming(144,true) with{FirstShare=75},2560,1080,144);
  var anchor=new DisplaySnapshot("P","PCI\\gpu","physical",-100,150,2560,1080,144,true);
  DisplayAdapterState a=new("V0","Root\\MttVDD","key0",false),b=new("V1","Root\\MttVDD","key1",true),foreign=new("Other","Root\\MttVDD","foreign",false);
  var plan=ManagedOutputAttachment.Plan([a,b,foreign],["key0","key1"],layouts,anchor);
  Check(plan.Count==1&&plan[0].DeviceName=="V0"&&plan[0].DeviceKey=="key0","Only the current detached owned adapter may attach.");
  Check(plan[0].X==2460&&plan[0].Y==150&&plan[0].Width==layouts[0].RenderWidth&&plan[0].Height==layouts[0].RenderHeight,"Attach beside the actual anchor with the requested render mode.");
  plan=ManagedOutputAttachment.Plan([a,b with{Attached=false}],["key0","key1"],layouts,anchor);
  Check(plan.Count==2&&plan[1].X==plan[0].X+plan[0].Width&&plan[1].Y==150,"New outputs must touch without overlapping.");
  Check(ManagedOutputAttachment.Plan([a with{Attached=true},b],["key0","key1"],layouts,anchor).Count==0,"Attached outputs must not be changed by preparation.");
  return Task.CompletedTask;
 }
 internal static Task IdentityBoundaries()
 {
  var layouts=LayoutEngine.Calculate(Presets.Balanced(60),2560,1080,60);
  var anchor=new DisplaySnapshot("P","PCI\\gpu","physical",0,0,2560,1080,144,true);
  DisplayAdapterState a=new("V0","Root\\MttVDD","key0",false),b=new("V1","Root\\MttVDD","key1",false);
  foreach(var inventory in new[]{new[]{a},new[]{a,b with{AdapterId="PCI\\other"}},new[]{a,b,b with{DeviceName="duplicate"}}})
   Check(ManagedOutputAttachment.Plan(inventory,["key0","key1"],layouts,anchor).Count==0,"Missing, wrong-driver or ambiguous identities must not be guessed.");
  Check(ManagedOutputAttachment.Plan([a,b],["old-key0","old-key1"],layouts,anchor).Count==0,"Historical DISPLAY names cannot substitute for current owned keys.");
  return Task.CompletedTask;
 }
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
