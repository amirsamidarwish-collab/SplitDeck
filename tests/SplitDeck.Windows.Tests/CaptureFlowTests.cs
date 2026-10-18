using SplitDeck.Windows;
internal static class CaptureFlowTests
{
 internal static Task Counters(){var flow=new CaptureFlow(1000);flow.Attempt();flow.Attempt();flow.Timeout();flow.Pointer();flow.Skip();flow.Image(1000,990);flow.Image(1020,1000);var s=flow.Snapshot;if(s.Attempts!=2||s.Timeouts!=1||s.PointerOnly!=1||s.CadenceSkips!=1||s.Images!=2||s.AverageAgeMs!=15||s.MaxAgeMs!=20||s.AverageImageIntervalMs!=20||s.MaxImageIntervalMs!=20)throw new Exception("Capture flow must distinguish missed acquisitions from slow image delivery.");return Task.CompletedTask;}
}
