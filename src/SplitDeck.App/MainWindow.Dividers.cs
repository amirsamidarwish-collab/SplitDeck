using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SplitDeck.Core;

namespace SplitDeck.App;
public partial class MainWindow
{
 private LayoutProfile? _dividerStart;
 private double _dividerDelta,_dividerPanelWidth;
 private void PlaceDividers(double x,double y,double width,double height)
 {
  bool triple=_profile.Third is not null;
  bool locked=_profile.Screens.Any(s=>s.VisibleWidth>0||s.VisibleHeight>0);
  DividerHint.Visibility=triple?Visibility.Visible:Visibility.Collapsed;
  DividerHint.Text=locked?"Set exact visible width and height to 0 (automatic) to unlock dragging.":$"Drag either green divider to resize · {_profile.FirstShare}% / {_profile.SecondShare}% / {100-_profile.FirstShare-_profile.SecondShare}% · Ratios stay locked";
  _dividerPanelWidth=width;
  Thumb[] handles=[LeftDivider,RightDivider];
  for(int i=0;i<handles.Length;i++){
   var handle=handles[i];handle.Visibility=triple?Visibility.Visible:Visibility.Collapsed;handle.IsEnabled=triple&&!locked&&!_busy&&!_invalidInput;
   handle.Opacity=handle.IsEnabled?1:.3;handle.Cursor=handle.IsEnabled?Cursors.SizeWE:Cursors.Arrow;
   handle.ToolTip=locked?"Exact visible dimensions lock the columns. Set them to 0 to drag.":"Drag to transfer space between the adjacent screens.";
   double share=i==0?_profile.FirstShare:_profile.FirstShare+_profile.SecondShare;
   Canvas.SetLeft(handle,x+width*share/100-8);Canvas.SetTop(handle,y);handle.Height=height;
  }
 }
 private void Divider_Started(object sender,DragStartedEventArgs e){_dividerStart=_profile;_dividerDelta=0;e.Handled=true;}
 private void Divider_Delta(object sender,DragDeltaEventArgs e)
 {
  if(_dividerStart is null||_monitor is null||_dividerPanelWidth<=0)return;
  _dividerDelta+=e.HorizontalChange;
  try{
   var candidate=ColumnResize.Move(_dividerStart,sender==LeftDivider?0:1,_dividerDelta/_dividerPanelWidth*100);
   LayoutEngine.Calculate(candidate,_monitor.Width,_monitor.Height,_monitor.RefreshHz);
   _profile=candidate;Render();
  }catch(ArgumentException){} // Keep the last fitting preview near a geometry limit.
  e.Handled=true;
 }
 private void Divider_Completed(object sender,DragCompletedEventArgs e)
 {
  if(_dividerStart is null)return;
  var original=_dividerStart;_dividerStart=null;
  if(e.Canceled){_profile=original;Render();}else Changed();
  e.Handled=true;
 }
 private void Divider_KeyDown(object sender,KeyEventArgs e)
 {
  if(e.Key==Key.Escape&&sender is Thumb thumb){thumb.CancelDrag();e.Handled=true;return;}
  if(e.Key is not(Key.Left or Key.Right))return;
  if(_monitor is null)return;
  try{var candidate=ColumnResize.Move(_profile,sender==LeftDivider?0:1,e.Key==Key.Left?-1:1);LayoutEngine.Calculate(candidate,_monitor.Width,_monitor.Height,_monitor.RefreshHz);_profile=candidate;Changed();}catch(ArgumentException){}
  e.Handled=true;
 }
}
