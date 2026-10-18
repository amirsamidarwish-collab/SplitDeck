using Forms=System.Windows.Forms;
namespace SplitDeck.Windows;
public sealed class RecoveryTray:IDisposable
{
 private readonly Forms.NotifyIcon icon;
 public RecoveryTray(Action restoreAndOpen)
 {
  var menu=new Forms.ContextMenuStrip();menu.Items.Add("Restore desktop / Open SplitDeck",null,(_,_)=>restoreAndOpen());
  icon=new Forms.NotifyIcon{Text="SplitDeck - Restore desktop",Icon=System.Drawing.SystemIcons.Application,ContextMenuStrip=menu,Visible=true};icon.DoubleClick+=(_,_)=>restoreAndOpen();
 }
 public void Dispose(){icon.Visible=false;icon.ContextMenuStrip?.Dispose();icon.Dispose();}
}
