using System.Windows.Controls;
using SplitDeck.Core;

namespace SplitDeck.App;

public partial class MainWindow
{
    // Runs only through the explicit preview-only diagnostic command. It exercises
    // our actual editor controls; it never applies a display layout or saves a profile.
    internal object VerifyEditorInputs()
    {
        if(!_previewOnly)throw new InvalidOperationException("Editor checks require an isolated preview instance.");
        _updating=true;_profile=Presets.Triple(Math.Min(60,PanelLimit));_selected=0;_customRatios[0]=true;_updating=false;Render();
        TextBox[] fields=[VisibleWidthInput,RenderWidthInput,RatioWidthInput,ExactRefreshInput,FirstShareInput];
        foreach(var field in fields){
            _updating=true;field.Focus();field.Text="17draft";field.Select(2,3);_updating=false;
            if(!field.IsKeyboardFocusWithin)throw new InvalidOperationException("Could not focus the editor's own diagnostic field.");
            Render(preserveDrafts:true);
            if(field.Text!="17draft"||field.SelectionStart!=2||field.SelectionLength!=3)throw new InvalidOperationException("A background editor refresh replaced an uncommitted draft or moved its selection: "+field.Name);
            if(DemandGpuBar.Value!=0||!DemandGpuValue.Text.StartsWith("Unavailable",StringComparison.Ordinal))throw new InvalidOperationException("Invalid preserved drafts must not display stale demand estimates.");
        }
        _updating=true;NameInput.Focus();_profile=Presets.Balanced(Math.Min(60,PanelLimit));_profile=_profile with{First=_profile.First with{RenderShortEdge=2160}};_customRatios[0]=false;_updating=false;Render();
        SelectByTag(RenderPicker,"Exact");
        if(_profile.First.RenderShortEdge!=0||_profile.First.RenderWidth!=0||_profile.First.RenderHeight!=0)throw new InvalidOperationException("Exact render mode retained a render-detail preset.");
        if(_layout is null||_layout[0].RenderWidth!=_layout[0].VisibleBounds.Width||_layout[0].RenderHeight!=_layout[0].VisibleBounds.Height)throw new InvalidOperationException("Automatic dimensions in exact mode did not return to native visible pixels.");
        _updating=true;_profile=Presets.Balanced(PanelLimit);_profile=_profile with{First=_profile.First with{RefreshHz=PanelLimit}};_selected=1;_updating=false;Render();
        var saved=_profile;ExactRefreshInput.Text="30";double low=DemandGpuBar.Value;string memory=DemandMemoryValue.Text;ExactRefreshInput.Text=Math.Min(60,PanelLimit).ToString();
        if(PanelLimit>30&&DemandGpuBar.Value<=low||memory!=DemandMemoryValue.Text||saved!=_profile)throw new InvalidOperationException("Refresh drafts must raise demand without inflating memory or committing the profile.");
        ExactRefreshInput.Text="999999";if(DemandGpuBar.Value!=0)throw new InvalidOperationException("Unsupported refresh retained old estimate.");
        RenderWidthInput.Text="oops";if(DemandGpuBar.Value!=0)throw new InvalidOperationException("Invalid input retained old estimate.");
        _updating=true;_profile=Presets.Triple(Math.Min(60,PanelLimit));_selected=1;_updating=false;Render();
        VerifyDividerDragging();
        return new{Result="PASS",FocusedDraftFields=fields.Length,DraftSelectionPreserved=true,ExactModeClearsPreset=true,LiveDemandDrafts=true,InvalidDemandCleared=true,DividerDragging=true,DividerCancellation=true,ExactVisibleLocks=true,ProfilePersistenceDisabled=true,DriverActionsDisabled=true,MutatedDisplaySettings=false};
    }

    private void VerifyDividerDragging()
    {
        var original=_profile;
        if(!LeftDivider.IsEnabled||!RightDivider.IsEnabled||LeftDivider.Visibility!=System.Windows.Visibility.Visible)throw new InvalidOperationException("Three-screen drag handles are unavailable.");
        var before=DemandMemoryValue.Text;
        static void Start(System.Windows.Controls.Primitives.Thumb t)=>t.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
        static void Drag(System.Windows.Controls.Primitives.Thumb t,double delta)=>t.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(delta,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});
        static void End(System.Windows.Controls.Primitives.Thumb t,bool cancel)=>t.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,cancel){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
        Start(LeftDivider);Drag(LeftDivider,_dividerPanelWidth*.10);
        if(_profile.FirstShare!=29||_profile.SecondShare!=52||DemandMemoryValue.Text==before)throw new InvalidOperationException("Left divider did not update its neighbors and live estimates.");
        End(LeftDivider,false);Start(RightDivider);Drag(RightDivider,-_dividerPanelWidth*.05);
        if(_profile.FirstShare!=29||_profile.SecondShare!=47)throw new InvalidOperationException("Right divider changed the wrong shares.");
        End(RightDivider,true);if(_profile.FirstShare!=29||_profile.SecondShare!=52)throw new InvalidOperationException("Canceled drag did not restore its initial layout.");
        _profile=original with{First=original.First with{VisibleWidth=486}};Render();if(LeftDivider.IsEnabled||RightDivider.IsEnabled)throw new InvalidOperationException("Exact visible sizes must lock automatic dividers.");
        _profile=Presets.Balanced(PanelLimit);Render();if(LeftDivider.Visibility!=System.Windows.Visibility.Collapsed||RightDivider.Visibility!=System.Windows.Visibility.Collapsed)throw new InvalidOperationException("Triple dividers leaked into two-screen layout.");
        _profile=original;Render();
    }
}
