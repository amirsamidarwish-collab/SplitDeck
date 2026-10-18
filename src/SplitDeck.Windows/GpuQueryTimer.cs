using Vortice.Direct3D11;

namespace SplitDeck.Windows;

internal sealed record GpuTimerSnapshot(bool Available,string? UnavailableReason,long Samples,int Pending,long Skipped,long InvalidSamples,double AverageMilliseconds,double MaxMilliseconds);

// Timestamp intervals on this immediate context, not whole-GPU busy time. Optional
// diagnostics must never wait for the GPU or force submission to obtain an answer.
internal sealed class GpuQueryTimer:IDisposable
{
 private readonly ID3D11DeviceContext context;private readonly List<Slot> all=[];private readonly Queue<Slot> available=new(),pending=new();
 private Slot? active;private long samples,skipped,invalid;private double total,maximum;private bool enabled=true,disposed;
 private GpuTimerSnapshot snapshot=new(true,null,0,0,0,0,0,0);
 internal GpuTimerSnapshot Snapshot=>Volatile.Read(ref snapshot);
 internal GpuQueryTimer(ID3D11Device device,ID3D11DeviceContext context)
 {
  this.context=context;
  try{for(int i=0;i<4;i++){var slot=new Slot();all.Add(slot);slot.Disjoint=device.CreateQuery(new(QueryType.TimestampDisjoint));slot.Start=device.CreateQuery(new(QueryType.Timestamp));slot.End=device.CreateQuery(new(QueryType.Timestamp));available.Enqueue(slot);}}
  catch(Exception e){Disable(e.Message);}
 }
 internal bool Begin(long frame)
 {
  if(!enabled||disposed||!GpuDiagnosticPolicy.ShouldSample(frame))return false;
  if(active is not null||!available.TryDequeue(out var slot)){skipped++;Publish();return false;}
  try{active=slot;context.Begin(slot.Disjoint!);context.End(slot.Start!);return true;}
  catch(Exception e){Disable(e.Message);return false;}
 }
 internal void End()
 {
  if(active is not { } slot||!enabled||disposed)return;
  try{context.End(slot.End!);context.End(slot.Disjoint!);active=null;pending.Enqueue(slot);Publish();}
  catch(Exception e){Disable(e.Message);}
 }
 internal void Poll()
 {
  if(!enabled||disposed)return;
  try{
   // At most four old slots, checked once each; S_FALSE exits immediately.
   while(pending.TryPeek(out var slot)){
    var disjointState=Read(slot.Disjoint!,out QueryDataTimestampDisjoint data);if(disjointState==GpuQueryReadState.Pending)return;if(disjointState==GpuQueryReadState.Unavailable){Disable("Timestamp-disjoint query failed.");return;}
    var startState=Read(slot.Start!,out ulong start);if(startState==GpuQueryReadState.Pending)return;if(startState==GpuQueryReadState.Unavailable){Disable("Start timestamp query failed.");return;}
    var endState=Read(slot.End!,out ulong end);if(endState==GpuQueryReadState.Pending)return;if(endState==GpuQueryReadState.Unavailable){Disable("End timestamp query failed.");return;}
    pending.Dequeue();available.Enqueue(slot);
    if(GpuDiagnosticPolicy.TryMilliseconds(start,end,data.Frequency,(bool)data.Disjoint,out double milliseconds)){samples++;total+=milliseconds;maximum=Math.Max(maximum,milliseconds);}else invalid++;
    Publish();
   }
  }catch(Exception e){Disable(e.Message);}
 }
 private unsafe GpuQueryReadState Read<T>(ID3D11Query query,out T value)where T:unmanaged
 {
  T data=default;var result=context.GetData(query,(nint)(&data),(uint)sizeof(T),AsyncGetDataFlags.DoNotFlush);var state=GpuDiagnosticPolicy.QueryState(result.Code);value=state==GpuQueryReadState.Ready?data:default;return state;
 }
 private void Publish()=>Volatile.Write(ref snapshot,new(enabled,Snapshot.UnavailableReason,samples,pending.Count,skipped,invalid,samples==0?0:total/samples,maximum));
 private void Disable(string reason){enabled=false;active=null;pending.Clear();available.Clear();foreach(var slot in all)slot.Dispose();all.Clear();Volatile.Write(ref snapshot,new(false,reason,samples,0,skipped,invalid,samples==0?0:total/samples,maximum));}
 public void Dispose(){if(disposed)return;disposed=true;foreach(var slot in all)slot.Dispose();all.Clear();pending.Clear();available.Clear();active=null;}
 private sealed class Slot:IDisposable{internal ID3D11Query? Disjoint,Start,End;public void Dispose(){Disjoint?.Dispose();Start?.Dispose();End?.Dispose();Disjoint=Start=End=null;}}
}
