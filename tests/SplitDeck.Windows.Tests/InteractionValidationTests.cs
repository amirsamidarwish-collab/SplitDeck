using SplitDeck.Windows;

internal static class InteractionValidationTests
{
 internal static Task CompleteEvidence()
 {
  Check(InteractionValidation.IsValid(Complete()),"Complete two-target evidence should pass.");
  foreach(var result in new[]{Complete() with{Completed=false},Complete() with{Canceled=true},Complete() with{Error="fixture failed"},Complete() with{CompletedCases=4},Complete() with{Cases=[]}})
   Check(!InteractionValidation.IsValid(result),"Incomplete, canceled or failed fixtures must fail.");
  Check(!InteractionValidation.IsValid(null),"Missing fixture must fail.");
  var missing=Complete() with{Cases=Complete().Cases.Skip(1).ToArray()};
  Check(!InteractionValidation.IsValid(missing),"Every phase of both targets is required.");
  var duplicate=Complete().Cases.ToArray();duplicate[1]=duplicate[0];
  Check(!InteractionValidation.IsValid(Complete() with{Cases=duplicate}),"Duplicate phases cannot substitute for missing phases.");
  return Task.CompletedTask;
 }
 internal static Task MissingWork()
 {
  var changes=new (int Case,Func<InteractionCaseMetrics,InteractionCaseMetrics> Change)[]{
   (0,x=>x with{Completed=false}),(0,x=>x with{TimerSamples=0}),(0,x=>x with{AnimationRequests=0}),(0,x=>x with{Paints=0}),(0,x=>x with{GapsOver250Ms=1}),
   (1,x=>x with{MenuOpened=0}),(1,x=>x with{MenuClosed=0}),(1,x=>x with{MenuItemInvocations=0}),
   (2,x=>x with{ScrollOperations=0}),(3,x=>x with{WindowMoves=0}),
   (4,x=>x with{MenuOpened=0}),(4,x=>x with{MenuClosed=0}),(4,x=>x with{MenuItemInvocations=0}),(4,x=>x with{ScrollOperations=0}),(4,x=>x with{WindowMoves=0})};
  foreach(var (index,change) in changes){var fixture=Complete();var cases=fixture.Cases.ToArray();cases[5+index]=change(cases[5+index]);Check(!InteractionValidation.IsValid(fixture with{Cases=cases}),"Required per-target work must be demonstrated: phase "+index);}
  return Task.CompletedTask;
 }
 private static InteractionFixtureResult Complete()
 {
  string[] names=["animation","context menus","text scrolling","window movement","combined"];
  var cases=Enumerable.Range(0,2).SelectMany(target=>Enumerable.Range(0,5).Select(index=>new InteractionCaseMetrics(index,names[index],target,"display"+target,80,16,20,24,0,0,index is 1 or 4?2:0,index is 1 or 4?2:0,index is 1 or 4?2:0,index is 2 or 4?4:0,index is 3 or 4?2:0,10,10,true))).ToArray();
  return new("own windows",false,true,false,20,5,cases,[],null);
 }
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
