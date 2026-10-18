using SplitDeck.Windows;

internal static class FrameInvalidationTests
{
 private static readonly CursorVisualState Arrow=new(true,1,123,50,50);
 internal static Task StaticFrames()
 {
  var policy=new FrameInvalidationPolicy(1000);Check(policy.NeedsPresent(0,false,Arrow,false),"First frame must present.");policy.Presented(0,Arrow);
  for(int now=1;now<1000;now++)Check(!policy.NeedsPresent(now,false,Arrow,false),"An unchanged desktop and cursor must not redraw at every poll.");
  Check(policy.NeedsPresent(1000,false,Arrow,false),"A one-second keepalive must retain renderer robustness.");policy.Presented(1000,Arrow);Check(!policy.NeedsPresent(1001,false,Arrow,false),"Keepalive must not cause catch-up bursts.");return Task.CompletedTask;
 }
 internal static Task ContentAndInspection()
 {
  var policy=new FrameInvalidationPolicy(1000);policy.Presented(0,Arrow);
  Check(policy.NeedsPresent(1,true,Arrow,false),"A desktop update must present immediately.");Check(policy.NeedsPresent(1,false,Arrow,true),"Diagnostic inspection must force a real render.");
  Check(!policy.NeedsPresent(1,false,Arrow,false),"Voting to present must not itself commit a successful presentation.");return Task.CompletedTask;
 }
 internal static Task CursorChanges()
 {
  var policy=new FrameInvalidationPolicy(1000);policy.Presented(0,Arrow);
  foreach(var state in new[]{Arrow with{X=51},Arrow with{Y=51},Arrow with{Handle=124},Arrow with{Flags=0},Arrow with{QuerySucceeded=false}})Check(policy.NeedsPresent(1,false,state,false),"Cursor position, appearance, visibility and query failure must invalidate the previous cursor.");
  var hidden=CursorVisualState.From(true,new Native.CursorInfo{Flags=0,Handle=123,Position=new(1,2)});var movedHidden=CursorVisualState.From(true,new Native.CursorInfo{Flags=0,Handle=124,Position=new(8,9)});
  Check(hidden==movedHidden,"Hidden cursor motion and handles must not force invisible redraw work.");
  Check(CursorVisualState.From(false,new Native.CursorInfo{Flags=1,Handle=123})==CursorVisualState.From(false,default),"Failed queries must not reuse stale native cursor fields.");
  policy.Presented(2,hidden);Check(policy.NeedsPresent(3,false,Arrow,false),"Showing a previously hidden cursor must redraw.");return Task.CompletedTask;
 }
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
