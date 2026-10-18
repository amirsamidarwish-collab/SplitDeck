namespace SplitDeck.Windows;

internal readonly record struct CursorVisualState(bool QuerySucceeded,uint Flags,nint Handle,int X,int Y)
{
 internal static CursorVisualState From(bool success,Native.CursorInfo cursor)=>!success?default:CursorProjection.Visible(cursor.Flags)?new(true,cursor.Flags,cursor.Handle,cursor.Position.X,cursor.Position.Y):new(true,cursor.Flags,0,0,0);
}
internal sealed class FrameInvalidationPolicy
{
 private readonly long keepalive;private long? lastPresented;private CursorVisualState lastCursor;
 internal FrameInvalidationPolicy(long ticksPerSecond){if(ticksPerSecond<=0)throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));keepalive=ticksPerSecond;}
 internal bool NeedsPresent(long now,bool contentChanged,CursorVisualState cursor,bool inspectFrame)=>lastPresented is not long last||contentChanged||cursor!=lastCursor||inspectFrame||now-last>=keepalive;
 internal void Presented(long now,CursorVisualState cursor){lastPresented=now;lastCursor=cursor;}
}
