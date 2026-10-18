using SplitDeck.Core;
using SplitDeck.Windows;

internal static class CursorBoundaryGuardTests
{
 internal static Task ReleasedClip()
 {
  var f=new Fixture{Clip=new(0,-1080,2560,2160),X=1480,Y=-570};var result=f.Guard().Tick();
  Check(result==new CursorBoundaryResult(true,true,true)&&f.Clip==f.VirtualBounds&&f.X==1480&&f.Y==0,"Released desktop clip must be repaired and escaped pointer brought onto an actual source.");return Task.CompletedTask;
 }
 internal static Task NarrowGameClip()
 {
  var f=new Fixture{Clip=new(100,100,800,600),X=300,Y=300};var clip=f.Clip;var result=f.Guard().Tick();
  Check(result==new CursorBoundaryResult(true,false,false)&&f.Clip==clip&&f.ClipWrites==0&&f.PositionWrites==0,"A valid narrower game clip and ordinary pointer must remain untouched.");
  var escaped=new Fixture{Clip=clip,X=-50,Y=-50};result=escaped.Guard().Tick();
  Check(result==new CursorBoundaryResult(true,false,true)&&escaped.Clip==clip&&escaped.X==100&&escaped.Y==100&&escaped.ClipWrites==0,"An escaped pointer must return inside the narrower game clip, rather than the unrestricted virtual bounds.");return Task.CompletedTask;
 }
 internal static Task PartialClip()
 {
  var f=new Fixture{Clip=new(-100,200,500,800),X=200,Y=300};var result=f.Guard().Tick();
  Check(result==new CursorBoundaryResult(true,true,false)&&f.Clip==new PixelRect(0,200,400,800)&&f.PositionWrites==0,"A partial game clip must be intersected with virtual bounds while retaining its restrictions.");return Task.CompletedTask;
 }
 internal static Task DisjointClip()
 {
  var f=new Fixture{Clip=new(0,-1080,2560,1080),X=1400,Y=-500};var result=f.Guard().Tick();
  Check(result.ClipChanged&&result.PositionChanged&&f.Clip==f.VirtualBounds&&f.X==1400&&f.Y==0,"A clip completely on the hidden physical output must reset to the virtual screens.");return Task.CompletedTask;
 }
 internal static Task PortraitGap()
 {
  var f=new Fixture{X=2200,Y=1078};var result=f.Guard().Tick();
  Check(result==new CursorBoundaryResult(true,false,true)&&f.X==2200&&f.Y==1071&&f.ClipWrites==0,"The eight-pixel gap below the portrait screen must project to its nearest actual pixel.");
  var gapClip=new Fixture{Clip=new(2000,1072,100,8),X=2050,Y=1078};result=gapClip.Guard().Tick();
  Check(result.ClipChanged&&result.PositionChanged&&gapClip.Clip==gapClip.VirtualBounds&&gapClip.Y==1071,"A clip wholly in the portrait gap has no usable screen and must be repaired.");return Task.CompletedTask;
 }
 internal static Task OrdinaryPointer()
 {
  var f=new Fixture{X=1439,Y=901};var guard=f.Guard();for(int i=0;i<12;i++)Check(guard.Tick()==new CursorBoundaryResult(true,false,false),"An ordinary pointer must not be warped.");
  Check(f.ClipWrites==0&&f.PositionWrites==0,"Steady-state guard must avoid native writes.");return Task.CompletedTask;
 }
 internal static Task FailedQuery()
 {
  foreach(bool clipFails in new[]{true,false}){
   var f=new Fixture{ClipQueryWorks=!clipFails,PositionQueryWorks=clipFails,Clip=new(0,-1080,2560,2160),X=1480,Y=-570};var result=f.Guard().Tick();
   Check(result==new CursorBoundaryResult(false,false,false)&&f.ClipWrites==0&&f.PositionWrites==0,"Failed input-desktop queries must skip every native mutation.");
  }
  return Task.CompletedTask;
 }
 internal static Task FailedWrite()
 {
  var clipFailure=new Fixture{ClipWriteWorks=false,Clip=new(0,-1080,2560,2160),Y=-10};
  try{clipFailure.Guard().Tick();throw new Exception("Expected clip failure to request recovery.");}catch(InvalidOperationException){}
  Check(clipFailure.PositionWrites==0,"Pointer must not be moved after a failed ClipCursor.");
  var positionFailure=new Fixture{PositionWriteWorks=false,X=2200,Y=1078};
  try{positionFailure.Guard().Tick();throw new Exception("Expected pointer failure to request recovery.");}catch(InvalidOperationException){}
  return Task.CompletedTask;
 }
 private sealed class Fixture
 {
  internal PixelRect VirtualBounds=>new(0,0,2523,1080);
  internal PixelRect Clip=new(0,0,2523,1080);internal int X=100,Y=100,ClipWrites,PositionWrites;
  internal bool ClipQueryWorks=true,PositionQueryWorks=true,ClipWriteWorks=true,PositionWriteWorks=true;
  internal CursorBoundaryGuard Guard()=>new([new(0,0,1920,1080),new(1920,0,603,1072)],()=> (ClipQueryWorks,Clip),()=> (PositionQueryWorks,X,Y),clip=>{ClipWrites++;if(!ClipWriteWorks)return false;Clip=clip;return true;},(x,y)=>{PositionWrites++;if(!PositionWriteWorks)return false;X=x;Y=y;return true;});
 }
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
