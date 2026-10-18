using SplitDeck.Core;
using SplitDeck.Windows;

internal static class WindowRescueTests
{
 internal static Task Geometry()
 {
  var source=new PixelRect(0,0,2560,1080);
  PixelRect[] targets=[new(0,0,585,1040),new(585,0,1920,1080)];
  PixelRect[] work=[new(0,0,585,1000),new(585,0,1920,1040)];
  var left=WindowRescuePolicy.Destination(new(0,20,500,700),source,targets,work);
  Check(left.Index==0&&Fits(left.Bounds,work[0]),"left original app enters portrait Screen1");
  var right=WindowRescuePolicy.Destination(new(1800,100,700,800),source,targets,work);
  Check(right.Index==1&&Fits(right.Bounds,work[1]),"right original app enters landscape Screen2");
  var huge=WindowRescuePolicy.Destination(new(-100,-100,4000,2400),source,targets,work);
  Check(Fits(huge.Bounds,work[huge.Index]),"oversize windows fit selected work area");
  var negativeSource=new PixelRect(-2560,-1080,2560,1080);
  var negative=WindowRescuePolicy.Destination(new(-2560,-1000,500,600),negativeSource,targets,work);
  Check(negative.Index==0&&Fits(negative.Bounds,work[0]),"negative desktop coordinates normalized");
  var monitor=new PixelRect(-1920,0,1920,1080);var topTaskbar=new PixelRect(-1880,40,1880,1040);var placement=new PixelRect(-1800,40,700,600);
  var screen=WindowRescuePolicy.ToScreen(placement,monitor,topTaskbar);
  Check(screen==new PixelRect(-1760,80,700,600),"top/left taskbar workspace offsets applied");
  Check(WindowRescuePolicy.ToWorkspace(screen,monitor,topTaskbar)==placement,"workspace conversion round trips without creeping");
  return Task.CompletedTask;
 }
 internal static Task Selection()
 {
  Check(WindowRescuePolicy.Eligible(true,false,false,false,false,false,false,false),"ordinary window selected");
  Check(!WindowRescuePolicy.Eligible(false,false,false,false,false,false,false,false),"hidden excluded");
  for(int flag=0;flag<7;flag++){
   var values=new bool[7];values[flag]=true;
   Check(!WindowRescuePolicy.Eligible(true,values[0],values[1],values[2],values[3],values[4],values[5],values[6]),"shell/helper/owned/tool/noactivate/child/cloaked excluded");
  }
  Check(WindowRescuePolicy.Family([(30,20),(20,10),(10,0),(40,999)]).SequenceEqual(new long[]{10,20,30}),"owned dialogs captured with selected owner regardless of enumeration order; unrelated owned popup excluded");
  Check(WindowRescuePolicy.Family([(1,2),(2,1)]).Count==0,"ownership cycles without selected root excluded");
  Check(WindowRescuePolicy.ModalTool(true,true,true),"tool-style modal dialog of disabled selected owner allowed");
  Check(!WindowRescuePolicy.ModalTool(true,false,true)&&!WindowRescuePolicy.ModalTool(true,true,false)&&!WindowRescuePolicy.ModalTool(false,true,true),"ordinary owned tools and arbitrary disabled helpers excluded");
  return Task.CompletedTask;
 }
 static bool Fits(PixelRect r,PixelRect area)=>r.X>=area.X&&r.Y>=area.Y&&r.Width>0&&r.Height>0&&(long)r.X+r.Width<=(long)area.X+area.Width&&(long)r.Y+r.Height<=(long)area.Y+area.Height;
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
