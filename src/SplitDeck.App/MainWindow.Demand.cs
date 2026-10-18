using System.Globalization;
using System.Windows.Controls;
using SplitDeck.Core;

namespace SplitDeck.App;
public partial class MainWindow
{
 private void DemandDraftChanged(object sender,TextChangedEventArgs e)
 {
  if(_updating)return;
  RefreshDraftDemand();
 }
 private void RefreshDraftDemand()
 {
  try{
   int Number(TextBox input){if(!int.TryParse(input.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)||value<0)throw new ArgumentException();return value;}
   var screen=SelectedScreen with{VisibleWidth=Number(VisibleWidthInput),VisibleHeight=Number(VisibleHeightInput),RenderWidth=Number(RenderWidthInput),RenderHeight=Number(RenderHeightInput),RefreshHz=Number(ExactRefreshInput)};
   if(screen.RenderWidth>0||screen.RenderHeight>0)screen=screen with{RenderShortEdge=0};
   if(RatioTag()=="Custom")screen=screen with{AspectWidth=Number(RatioWidthInput),AspectHeight=Number(RatioHeightInput)};
   var draft=_selected switch{0=>_profile with{First=screen},1=>_profile with{Second=screen},_=>_profile with{Third=screen}};
   if(draft.Third is not null)draft=draft with{FirstShare=Number(FirstShareInput),SecondShare=Number(SecondShareInput)};
   RefreshDemand(draft);
  }catch(ArgumentException){ClearDemand();}
 }
 private void RefreshDemand(LayoutProfile profile)
 {
  try{
   if(_monitor is null||profile.Screens.Any(s=>s.RefreshHz>_monitor.RefreshHz))throw new ArgumentException();
   var layout=LayoutEngine.Calculate(profile,_monitor.Width,_monitor.Height,_monitor.RefreshHz);
   var demand=LayoutDemandEstimator.Estimate(layout,_monitor.Width,_monitor.Height,_monitor.RefreshHz);
   DemandGpuBar.Value=Math.Min(100,demand.WorkloadRatio*100);DemandMemoryBar.Value=Math.Min(100,demand.BufferRatio*100);
   DemandGpuValue.Text=$"{demand.WorkloadRatio:0.00}× native reference · {demand.MegapixelsPerSecond:0} MP/s";
   DemandMemoryValue.Text=$"≈{demand.BufferMiB:0.0} MiB · {demand.BufferRatio:0.00}× native reference";
   DemandNote.Text=$"Reference: {_monitor.Width} × {_monitor.Height} at {_monitor.RefreshHz:0.##} Hz. Bars fill at 1×; values above 1× remain visible. Estimates, not GPU utilization or capacity limits.";
  }catch(Exception e)when(e is ArgumentException or OverflowException){ClearDemand();}
 }
 private void ClearDemand(){DemandGpuBar.Value=DemandMemoryBar.Value=0;DemandGpuValue.Text=DemandMemoryValue.Text="Unavailable · finish a valid layout";DemandNote.Text="Enter valid dimensions, ratios and refresh targets to estimate demand.";}
}
