using SplitDeck.Core;
using SplitDeck.Windows;
internal static class SessionTopologyTests
{
 internal static Task ConnectedTriple()
 {
  VirtualScreenLayout[] layouts=[new(0,"Left",new(0,0,486,864),486,864,30),new(1,"Center",new(486,0,1584,891),1584,891,60),new(2,"Right",new(2070,0,486,864),486,864,30)];
  var virtuals=SessionDisplayPlan.Build(Enumerable.Range(0,3).Select(i=>new MonitorInfo("virtual"+i,"Virtual",0,0,100,100,60,null,false,true)).ToArray(),layouts,1);
  DisplaySnapshot[] originals=[new("physical","gpu","key",0,0,2560,1080,144,true)];
  var physical=SessionDisplayPlan.PhysicalModes(originals,"physical",virtuals,null).Single();
  Check(physical.X==0&&physical.Y==-1080,"The physical presenter must touch the center primary instead of forming a disconnected island.");
  Check(physical.Y+physical.Height==virtuals[1].Y,"Physical bottom and primary top share an edge.");
  Check(virtuals[0].X==-486&&virtuals[0].Y==0&&virtuals[2].X==1584&&virtuals[2].Y==0,"Negative-left and positive-right screens retain their exact adjacent source coordinates.");
  Check(virtuals.All(v=>!Overlap(physical,new(v.DeviceName,"virtual",v.DeviceName,v.X,v.Y,v.Width,v.Height,v.Hz,v.Primary))),"Physical presentation cannot overlap any virtual source.");
  Check(originals[0].Y==0&&originals[0].Hz==144&&originals[0].Primary,"Original recovery state remains unchanged.");
  return Task.CompletedTask;
 }
 internal static Task PhysicalGroup()
 {
  DisplaySnapshot[] originals=[new("selected","gpu","selected",-2560,-400,2560,1080,144,false),new("other","gpu2","other",0,0,1920,1200,75,true)];
  SessionDisplayMode[] virtuals=[new("left",-500,0,500,900,30,false),new("primary",0,0,1600,900,60,true),new("right",1600,0,500,900,30,false)];
  var physical=SessionDisplayPlan.PhysicalModes(originals,"selected",virtuals,60);
  Check(physical[0].X-physical[1].X==-2560&&physical[0].Y-physical[1].Y==-400,"Moving the negative-X physical group preserves all relative offsets.");
  Check(physical.Max(m=>m.Y+m.Height)==0&&physical[1].X==0,"A bottommost physical output must share a positive-length edge with the virtual primary.");
  Check(physical[0].Hz==60&&physical[1].Hz==75&&physical.All(m=>!m.Primary),"Only selected output Hz changes and virtual primary remains exclusive.");
  Check(physical.All(p=>virtuals.All(v=>!Overlap(p,new(v.DeviceName,"virtual",v.DeviceName,v.X,v.Y,v.Width,v.Height,v.Hz,v.Primary)))),"The translated physical group never overlaps virtual sources.");
  return Task.CompletedTask;
 }
 static bool Overlap(DisplaySnapshot a,DisplaySnapshot b)=>Math.Max(a.X,b.X)<Math.Min((long)a.X+a.Width,(long)b.X+b.Width)&&Math.Max(a.Y,b.Y)<Math.Min((long)a.Y+a.Height,(long)b.Y+b.Height);
 static void Check(bool value,string detail){if(!value)throw new Exception(detail);}
}
