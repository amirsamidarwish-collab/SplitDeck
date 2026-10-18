using SplitDeck.Core;

namespace SplitDeck.Windows;

internal readonly record struct CursorBoundaryResult(bool QueriesSucceeded,bool ClipChanged,bool PositionChanged);
internal sealed class CursorBoundaryGuard
{
 private readonly PixelRect[] screens;private readonly PixelRect virtualBounds;
 private readonly Func<(bool Success,PixelRect Bounds)> readClip;private readonly Func<(bool Success,int X,int Y)> readPosition;
 private readonly Func<PixelRect,bool> writeClip;private readonly Func<int,int,bool> writePosition;
 internal CursorBoundaryGuard(IReadOnlyList<PixelRect> screens,Func<(bool Success,PixelRect Bounds)> readClip,Func<(bool Success,int X,int Y)> readPosition,Func<PixelRect,bool> writeClip,Func<int,int,bool> writePosition)
 {
  ArgumentNullException.ThrowIfNull(screens);ArgumentNullException.ThrowIfNull(readClip);ArgumentNullException.ThrowIfNull(readPosition);ArgumentNullException.ThrowIfNull(writeClip);ArgumentNullException.ThrowIfNull(writePosition);
  if(screens.Count==0||screens.Any(s=>s.Width<=0||s.Height<=0||Right(s)>int.MaxValue||Bottom(s)>int.MaxValue))throw new ArgumentException("Valid virtual source rectangles are required.",nameof(screens));
  this.screens=screens.ToArray();int left=screens.Min(s=>s.X),top=screens.Min(s=>s.Y);long width=screens.Max(Right)-left,height=screens.Max(Bottom)-top;
  if(width>int.MaxValue||height>int.MaxValue)throw new ArgumentException("Virtual source bounds exceed the supported coordinate range.",nameof(screens));
  virtualBounds=new(left,top,(int)width,(int)height);this.readClip=readClip;this.readPosition=readPosition;this.writeClip=writeClip;this.writePosition=writePosition;
 }
 internal CursorBoundaryResult Tick()
 {
  // On another input desktop these queries can fail. Never write a clip or
  // pointer position after either failed query.
  var current=readClip();if(!current.Success)return new(false,false,false);
  var cursor=readPosition();if(!cursor.Success)return new(false,false,false);
  PixelRect effective=Intersect(current.Bounds,virtualBounds)??virtualBounds;
  bool usable=false;foreach(var screen in screens)if(Intersect(screen,effective) is not null){usable=true;break;}
  if(!usable)effective=virtualBounds;
  bool clipChanged=effective!=current.Bounds;
  int targetX=cursor.X,targetY=cursor.Y;double best=double.PositiveInfinity;
  foreach(var screen in screens){
   if(Intersect(screen,effective) is not { } area)continue;
   int x=(int)Math.Clamp((long)cursor.X,area.X,Right(area)-1),y=(int)Math.Clamp((long)cursor.Y,area.Y,Bottom(area)-1);
   double dx=(double)x-cursor.X,dy=(double)y-cursor.Y,distance=dx*dx+dy*dy;
   if(distance<best){best=distance;targetX=x;targetY=y;}
   if(distance==0)break;
  }
  bool positionChanged=targetX!=cursor.X||targetY!=cursor.Y;
  if(clipChanged&&!writeClip(effective))throw new InvalidOperationException("Windows could not restore the virtual screen cursor boundary; restoring desktop.");
  if(positionChanged&&!writePosition(targetX,targetY))throw new InvalidOperationException("Windows could not return the pointer to a visible virtual screen; restoring desktop.");
  return new(true,clipChanged,positionChanged);
 }
 private static long Right(PixelRect rect)=>(long)rect.X+rect.Width;
 private static long Bottom(PixelRect rect)=>(long)rect.Y+rect.Height;
 private static PixelRect? Intersect(PixelRect first,PixelRect second)
 {
  if(first.Width<=0||first.Height<=0||second.Width<=0||second.Height<=0)return null;
  long left=Math.Max(first.X,second.X),top=Math.Max(first.Y,second.Y),right=Math.Min(Right(first),Right(second)),bottom=Math.Min(Bottom(first),Bottom(second));
  return right>left&&bottom>top?new((int)left,(int)top,(int)(right-left),(int)(bottom-top)):null;
 }
}
