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
        return new{Result="PASS",FocusedDraftFields=fields.Length,DraftSelectionPreserved=true,ExactModeClearsPreset=true,LiveDemandDrafts=true,InvalidDemandCleared=true,ProfilePersistenceDisabled=true,DriverActionsDisabled=true,MutatedDisplaySettings=false};
    }
}
