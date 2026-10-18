namespace SplitDeck.Windows;

internal static class InteractionValidation
{
 private static readonly string[] Phases=["animation","context menus","text scrolling","window movement","combined"];
 internal static bool IsValid(InteractionFixtureResult? result,int expectedTargets=2)
 {
  if(expectedTargets is <1 or >4||result is null||!result.Completed||result.Canceled||result.Error is not null||
     !double.IsFinite(result.ElapsedSeconds)||result.ElapsedSeconds<=0||result.CompletedCases!=Phases.Length||
     result.Cases is null||result.Cases.Count!=expectedTargets*Phases.Length)return false;
  for(int target=0;target<expectedTargets;target++){
   var cases=result.Cases.Where(x=>x.TargetIndex==target).ToArray();
   if(cases.Length!=Phases.Length||cases.Select(x=>x.DeviceName).Distinct(StringComparer.Ordinal).Count()!=1)return false;
   for(int phase=0;phase<Phases.Length;phase++){
    var matches=cases.Where(x=>x.CaseIndex==phase).ToArray();if(matches.Length!=1)return false;
    var metric=matches[0];
    if(metric.Case!=Phases[phase]||string.IsNullOrWhiteSpace(metric.DeviceName)||!metric.Completed||
       metric.TimerSamples<=0||metric.AnimationRequests<=0||metric.Paints<=0||metric.GapsOver250Ms!=0||
       !double.IsFinite(metric.MaximumTimerGapMs)||metric.MaximumTimerGapMs is <=0 or >250)return false;
    if(phase is 1 or 4&&(metric.MenuOpened<=0||metric.MenuClosed<=0||metric.MenuItemInvocations<=0))return false;
    if(phase is 2 or 4&&metric.ScrollOperations<=0)return false;
    if(phase is 3 or 4&&metric.WindowMoves<=0)return false;
   }
  }
  return true;
 }
}
