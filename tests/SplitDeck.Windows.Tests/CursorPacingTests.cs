using SplitDeck.Core;
using SplitDeck.Windows;

internal static class CursorPacingTests
{
 internal static Task CursorProjection()
 {
  var source=new MonitorInfo("test","test",2000,-100,2000,1000,60,null,false,true);
  var area=new PixelRect(20,30,1000,500);
  var p=SplitDeck.Windows.CursorProjection.Project(source,area,2100,0,32,48,5,7);
  Check(p.X==67.5f&&p.Y==76.5f&&p.Width==16&&p.Height==24,"Cursor must scale native dimensions and subtract the native hotspot.");
  Check(Math.Abs(p.SourceU-.0475f)<.0001&&Math.Abs(p.SourceV-.093f)<.0001,"Cursor desktop UV must use source coordinates, including negative desktop origins.");
  Check(SplitDeck.Windows.CursorProjection.Visible(1)&&!SplitDeck.Windows.CursorProjection.Visible(0)&&!SplitDeck.Windows.CursorProjection.Visible(2),"Hidden/suppressed cursors must not be projected.");return Task.CompletedTask;
 }
 internal static Task CursorReuse()
 {
  var cache=new CursorShapeCache<string>();int calls=0;int frees=0;
  string Read(nint handle){calls++;return handle.ToString();}
  Check(cache.Get(1,Read,_=>frees++)=="1"&&cache.Get(1,Read,_=>frees++)=="1"&&calls==1,"Unchanged cursor shape must reuse resources.");
  cache.Get(2,Read,_=>frees++);Check(calls==2&&frees==1,"Changed native cursor must release the previous resources once.");
  try{cache.Get(3,_=>throw new IOException("shape failed"),_=>frees++);throw new Exception("Expected read failure.");}catch(IOException){}
  Check(cache.Get(2,Read,_=>frees++)=="2"&&calls==2,"Failed replacement must retain valid cached resources.");
  cache.Clear(_=>frees++);cache.Clear(_=>frees++);Check(frees==2,"Disposal must release resources once.");return Task.CompletedTask;
 }
 internal static Task PresentationDeadline()
 {
  var clock=new PresentationCadence(1_000_000,60);long now=0;int frames=0;long physical=1_000_000/144;
  while(now<1_000_000){now+=clock.WaitTicks(now);now=(now/physical+1)*physical;frames++;}
  Check(frames>=59&&frames<=61,$"60 Hz presentation on 144 Hz must remain near 60, got {frames}.");
  var delayed=new PresentationCadence(1000,30);Check(delayed.WaitTicks(0)==0,"First frame should start immediately.");
  Check(delayed.WaitTicks(200)==0,"A delayed frame should run once.");
  Check(delayed.WaitTicks(201)>0,"Delayed frames must skip stale deadlines instead of bursting.");return Task.CompletedTask;
 }
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}

