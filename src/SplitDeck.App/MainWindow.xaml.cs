using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SplitDeck.Core;
using SplitDeck.Windows;
using Path = System.IO.Path;

namespace SplitDeck.App;

public partial class MainWindow : Window
{
    private readonly SessionController _session = new();
    private readonly ProfileStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitDeck"));
    private readonly Dictionary<string, double> _manualSizes = new(StringComparer.OrdinalIgnoreCase);
    private LayoutProfile _profile = Presets.Balanced(60);
    private IReadOnlyList<VirtualScreenLayout>? _layout;
    private MonitorInfo? _monitor;
    private int _selected;
    private readonly bool[] _customRatios = new bool[3];
    private bool _updating = true;
    private bool _busy;
    private bool _invalidInput;
    private bool _closingAfterRestore;
    private RecoveryTray? _tray;
    private readonly bool _previewOnly;
    private int _readinessCount=2;

    public MainWindow():this(null){}
    public MainWindow(LayoutProfile? previewProfile)
    {
        _previewOnly=previewProfile is not null;
        InitializeComponent();
        foreach(var input in EditableInputs)input.TextChanged+=DemandDraftChanged;
        var area=SystemParameters.WorkArea;MinWidth=Math.Min(MinWidth,Math.Max(640,area.Width-32));MinHeight=Math.Min(MinHeight,Math.Max(480,area.Height-32));Width=Math.Min(Width,area.Width-32);Height=Math.Min(Height,area.Height-32);
        _updating = false;
        _session.StatusChanged += OnSessionStatus;
        _session.SessionEnded += OnSessionEnded;
        Loaded += async (_, _) => await RefreshDriverReadiness();
        _tray=new RecoveryTray(()=>Dispatcher.BeginInvoke(async()=>{await RunOperation(async()=>{await _session.RestoreAsync();Show();WindowState=WindowState.Normal;Activate();});}));
        try
        {
            var loaded = previewProfile??_store.Load();
            if (loaded is not null) _profile = loaded;
            else if (File.Exists(Path.Combine(Environment.GetFolderPath(
                         Environment.SpecialFolder.LocalApplicationData), "SplitDeck", "profile.json")))
                ErrorText.Text = "Saved profile was invalid. A balanced profile is shown instead.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = $"Could not load saved profile: {ex.Message}";
        }

        _customRatios[0] = IsCustomRatio(_profile.First);
        _customRatios[1] = IsCustomRatio(_profile.Second);
        if(_profile.Third is not null)_customRatios[2]=IsCustomRatio(_profile.Third);

        DiscoverMonitors();
    }

    private void DiscoverMonitors()
    {
        try
        {
            var monitors = new DisplayService().Detect().Where(m => !m.IsVirtual).ToArray();
            _updating = true;
            MonitorPicker.ItemsSource = monitors;
            MonitorPicker.SelectedItem = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            _monitor = MonitorPicker.SelectedItem as MonitorInfo;
            _updating = false;
            if (_monitor is null)
            {
                StatusText.Text = "No physical monitor detected";
                StatusDetail.Text = "Connect a physical display, then reopen SplitDeck. Apply is unavailable.";
            }
            else
            {
                StatusText.Text = "Local preview ready";
                StatusDetail.Text = !_session.IsDriverReadinessKnown ? "Checking the installed driver in the background. Editing is available."
                    : _session.IsDriverInstalled
                    ? "Editing is local until Apply. The installed driver is ready."
                    : "Driver setup is required before Apply. Editing is local until Apply.";
            }
            Render();
        }
        catch (Exception ex)
        {
            _updating = false;
            StatusText.Text = "Monitor discovery failed";
            ErrorText.Text = ex.Message;
            Render();
        }
    }

    private void Render(bool preserveDrafts=false)
    {
        var drafts=preserveDrafts?EditableInputs.Where(field=>field.IsKeyboardFocusWithin)
            .Select(field=>new InputDraft(field,field.Text,field.SelectionStart,field.SelectionLength)).ToArray():Array.Empty<InputDraft>();
        _updating = true;
        try
        {
            _selected=Math.Clamp(_selected,0,_profile.Screens.Count-1);
            var screen = SelectedScreen;
            bool triple=_profile.Third is not null;
            CountDescription.Text=$"{(triple?"Three":"Two")} virtual displays. One physical monitor.";
            TwoScreensButton.BorderBrush=!triple?(Brush)Resources["Accent"]:(Brush)Resources["Line"];
            ThreeScreensButton.BorderBrush=triple?(Brush)Resources["Accent"]:(Brush)Resources["Line"];
            TripleButton.BorderBrush=_profile==Presets.Triple(PanelLimit)?(Brush)Resources["Accent"]:(Brush)Resources["Line"];
            TwoSharePanel.Visibility=triple?Visibility.Collapsed:Visibility.Visible;
            ThreeSharePanel.Visibility=triple?Visibility.Visible:Visibility.Collapsed;
            FirstShareInput.Text=_profile.FirstShare.ToString(CultureInfo.InvariantCulture);SecondShareInput.Text=_profile.SecondShare.ToString(CultureInfo.InvariantCulture);ThirdShareLabel.Text=$"/ {100-_profile.FirstShare-_profile.SecondShare}% Screen 3";
            SummariesGrid.Columns=EditTabs.Columns=_profile.Screens.Count;EditThree.Visibility=SummaryThree.Visibility=triple?Visibility.Visible:Visibility.Collapsed;
            MoveLeftButton.IsEnabled=_selected>0;MoveRightButton.IsEnabled=_selected<_profile.Screens.Count-1;
            MakePrimaryButton.IsEnabled=_selected!=_profile.PrimaryIndex;PrimaryNote.Text=$"Screen {_profile.PrimaryIndex+1} · {_profile.Screens[_profile.PrimaryIndex].Name} is primary.";
            PriorityToggle.IsEnabled=!triple;
            ShareSlider.Value = _profile.FirstShare;
            ShareLabel.Text = $"{_profile.FirstShare}%";
            PriorityToggle.IsChecked = _profile.Priority;
            NameInput.Text = screen.Name;
            RatioWidthInput.Text = screen.AspectWidth.ToString(CultureInfo.InvariantCulture);
            RatioHeightInput.Text = screen.AspectHeight.ToString(CultureInfo.InvariantCulture);
            VisibleWidthInput.Text=screen.VisibleWidth.ToString(CultureInfo.InvariantCulture);VisibleHeightInput.Text=screen.VisibleHeight.ToString(CultureInfo.InvariantCulture);
            RenderWidthInput.Text=screen.RenderWidth.ToString(CultureInfo.InvariantCulture);RenderHeightInput.Text=screen.RenderHeight.ToString(CultureInfo.InvariantCulture);ExactRefreshInput.Text=screen.RefreshHz.ToString(CultureInfo.InvariantCulture);
            SelectRatio(screen);
            CustomRatioPanel.Visibility = RatioTag() == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            RatioNote.Text = screen.Fill ? "Uses all pixels in this screen's allotted area." :
                $"{screen.AspectWidth}:{screen.AspectHeight} preserved in {(screen.Portrait ? "portrait" : "landscape")}.";
            SelectByTag(RenderPicker, screen.RenderWidth>0||screen.RenderHeight>0?"Exact":screen.RenderShortEdge.ToString(CultureInfo.InvariantCulture));
            RefreshChoices();
            EditOne.BorderBrush = _selected == 0 ? Brushes.YellowGreen : (Brush)Resources["Line"];
            EditTwo.BorderBrush = _selected == 1 ? Brushes.YellowGreen : (Brush)Resources["Line"];
            EditThree.BorderBrush = _selected == 2 ? Brushes.YellowGreen : (Brush)Resources["Line"];
            LandscapeButton.BorderBrush = !screen.Portrait ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            PortraitOrientationButton.BorderBrush = screen.Portrait ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            EqualButton.BorderBrush = IsEqualPreset() ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            GameButton.BorderBrush = IsGamingPreset(false) ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            PortraitButton.BorderBrush = IsGamingPreset(true) ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            bool driverReady = _readinessCount==_profile.Screens.Count&&_session.IsDriverInstalled;
            SetupButton.Visibility = driverReady ? Visibility.Collapsed : Visibility.Visible;
            SetupButton.IsEnabled=!_busy&&!_previewOnly;
            RestoreButton.IsEnabled = !_busy && _session.IsActive;
            PanelNote.Text = _monitor is null ? "A physical display is needed for preview." :
                $"One physical panel · {_monitor.RefreshHz:0.##} Hz detected output cadence. Refresh targets do not guarantee game FPS.";
            SizeInput.Text = _monitor is not null && _manualSizes.TryGetValue(_monitor.DeviceName, out var inches)
                ? inches.ToString("0.0", CultureInfo.InvariantCulture) : "";
            DrawPreview();
            RefreshDemand(_profile);
            ApplyButton.IsEnabled = !_previewOnly&&!_busy && !_invalidInput && driverReady &&
                _monitor is not null && _layout is not null && !_session.IsActive;
        }
        finally
        {
            foreach(var draft in drafts){draft.Field.Text=draft.Text;draft.Field.Select(draft.SelectionStart,draft.SelectionLength);}
            _updating = false;
            if(drafts.Length>0)RefreshDraftDemand();
        }
    }
    private sealed record InputDraft(TextBox Field,string Text,int SelectionStart,int SelectionLength);
    private IEnumerable<TextBox> EditableInputs=>[NameInput,RatioWidthInput,RatioHeightInput,VisibleWidthInput,VisibleHeightInput,RenderWidthInput,RenderHeightInput,ExactRefreshInput,FirstShareInput,SecondShareInput,SizeInput];

    private ScreenSettings SelectedScreen => _profile.Screens[_selected];

    private void SelectRatio(ScreenSettings screen)
    {
        var tag = screen.Fill ? "Fill" : _customRatios[_selected] ? "Custom" : (screen.AspectWidth, screen.AspectHeight) switch
        {
            (16, 9) => "16:9", (16, 10) => "16:10", (21, 9) => "21:9", _ => "Custom"
        };
        SelectByTag(RatioPicker, tag);
    }

    private static bool IsCustomRatio(ScreenSettings screen) => !screen.Fill &&
        (screen.AspectWidth, screen.AspectHeight) is not ((16, 9) or (16, 10) or (21, 9));

    private static void SelectByTag(ComboBox picker, string tag)
    {
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private string? RatioTag() => (RatioPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private void RefreshChoices()
    {
        var limit = _monitor is null ? 60 : Math.Max(1, (int)Math.Floor(_monitor.RefreshHz));
        var values = new[] { 30, 60, 120, 144, 165, 240, limit, SelectedScreen.RefreshHz }
            .Where(hz => hz > 0 && hz <= limit).Distinct().OrderByDescending(hz => hz).ToArray();
        RefreshPicker.Items.Clear();
        foreach (var hz in values)
            RefreshPicker.Items.Add(new ComboBoxItem { Content = $"{hz} Hz target" + (hz == limit ? " · panel limit" : ""), Tag = hz });
        RefreshPicker.SelectedItem = RefreshPicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (int)item.Tag == SelectedScreen.RefreshHz);
        RefreshNote.Text = SelectedScreen.RefreshHz > limit
            ? $"Saved target exceeds this panel's {limit} Hz limit. Choose a supported target before Apply."
            : "Virtual and compositor targets share the one physical output cadence.";
    }

    private void DrawPreview()
    {
        _layout = null;
        PreviewOne.Visibility = Visibility.Collapsed;
        PreviewTwo.Visibility = Visibility.Collapsed;
        PreviewThree.Visibility = Visibility.Collapsed;
        if (_monitor is null)
        {
            MonitorMeta.Text = "No selectable physical display";
            SizeLabel.Text = "Size unavailable";
            FitNote.Text = "Connect a physical display to calculate a layout.";
            ClearSummaries();
            return;
        }

        MonitorMeta.Text = $"{_monitor.Width} × {_monitor.Height} · {_monitor.RefreshHz:0.##} Hz";
        var manual = _manualSizes.TryGetValue(_monitor.DeviceName, out var inches);
        var diagonal = manual ? inches : _monitor.DiagonalInches;
        SizeLabel.Text = diagonal is > 0
            ? $"≈{diagonal:0.0}″ {(manual ? "manual size" : "reported size")}" : "Size unavailable";
        try
        {
            if (_profile.Screens.Any(s=>s.RefreshHz>_monitor.RefreshHz))
                throw new ArgumentException("A refresh target exceeds this panel's detected limit. Choose a lower target.");
            _layout = LayoutEngine.Calculate(_profile, _monitor.Width, _monitor.Height, _monitor.RefreshHz);
            var scale = Math.Min(720d / _monitor.Width, 260d / _monitor.Height);
            var panelW = _monitor.Width * scale;
            var panelH = _monitor.Height * scale;
            var offsetX = (720 - panelW) / 2;
            var offsetY = (260 - panelH) / 2;
            PreviewCanvas.Children.OfType<Rectangle>().ToList().ForEach(r => PreviewCanvas.Children.Remove(r));
            var outline = new Rectangle { Width = panelW, Height = panelH,
                Stroke = (Brush)Resources["Line"], StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromRgb(15, 21, 16)) };
            PreviewCanvas.Children.Insert(0, outline);
            Canvas.SetLeft(outline, offsetX); Canvas.SetTop(outline, offsetY);
            Place(PreviewOne, _layout[0].VisibleBounds, offsetX, offsetY, scale);
            Place(PreviewTwo, _layout[1].VisibleBounds, offsetX, offsetY, scale);
            if(_layout.Count==3)Place(PreviewThree,_layout[2].VisibleBounds,offsetX,offsetY,scale);
            PreviewOneName.Text = _profile.First.Name;
            PreviewTwoName.Text = _profile.Second.Name;
            PreviewOneDims.Text = Dimensions(_layout[0].VisibleBounds);
            PreviewTwoDims.Text = Dimensions(_layout[1].VisibleBounds);
            if(_layout.Count==3){PreviewThreeName.Text=_profile.Third!.Name;PreviewThreeDims.Text=Dimensions(_layout[2].VisibleBounds);PreviewThree.BorderBrush=_selected==2?(Brush)Resources["Accent"]:(Brush)Resources["Line"];SummaryThree.BorderBrush=PreviewThree.BorderBrush;SetSummary(2,_profile.Third,_layout[2]);}
            PreviewOne.BorderBrush = _selected == 0 ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            PreviewTwo.BorderBrush = _selected == 1 ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            SummaryOne.BorderBrush = _selected == 0 ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            SummaryTwo.BorderBrush = _selected == 1 ? (Brush)Resources["Accent"] : (Brush)Resources["Line"];
            SetSummary(0, _profile.First, _layout[0]);
            SetSummary(1, _profile.Second, _layout[1]);
            var used = _layout.Sum(s => (double)s.VisibleBounds.Width * s.VisibleBounds.Height);
            FitNote.Text = $"{100 * (1 - used / (_monitor.Width * (double)_monitor.Height)):0.0}% unused physical area · {_layout.Count} screens · Primary: {_profile.PrimaryIndex+1}. Exact dimensions retain their ratio.";
            var current = _layout[_selected];
            RenderNote.Text = current.RenderWidth > current.VisibleBounds.Width || current.RenderHeight > current.VisibleBounds.Height
                ? "Downsampled to visible pixels. Higher detail adds GPU work, not physical pixels."
                : current.RenderWidth < current.VisibleBounds.Width || current.RenderHeight < current.VisibleBounds.Height
                    ? "Upscaled to visible pixels while preserving aspect ratio."
                    : "Render size matches visible pixels; no scaling.";
            if (ErrorText.Text.StartsWith("Layout:", StringComparison.Ordinal)) ErrorText.Text = "";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            ErrorText.Text = $"Layout: {ex.Message}";
            FitNote.Text = "This layout cannot fit the selected monitor.";
            ClearSummaries();
        }
    }

    private static void Place(Border border, PixelRect rect, double x, double y, double scale)
    {
        border.Width = rect.Width * scale;
        border.Height = rect.Height * scale;
        Canvas.SetLeft(border, x + rect.X * scale);
        Canvas.SetTop(border, y + rect.Y * scale);
        border.Visibility = Visibility.Visible;
    }

    private static string Dimensions(PixelRect rect) => $"{rect.Width} × {rect.Height}";
    private static string Dimensions(int w, int h) => $"{w} × {h}";

    private void ClearSummaries()
    {
        SummaryOneName.Text = $"1 · {_profile.First.Name}";
        SummaryTwoName.Text = $"2 · {_profile.Second.Name}";
        SummaryThreeName.Text=$"3 · {_profile.Third?.Name}";
        SummaryOnePhysical.Text = SummaryTwoPhysical.Text = SummaryThreePhysical.Text="—";
        SummaryOneRender.Text = SummaryTwoRender.Text = SummaryThreeRender.Text="Render —";
        SummaryOneHz.Text = SummaryTwoHz.Text = SummaryThreeHz.Text="Target —";
        RenderNote.Text = "Choose a valid layout to see render details.";
    }

    private void SetSummary(int index, ScreenSettings screen, VirtualScreenLayout layout)
    {
        var name = index == 0 ? SummaryOneName : index==1?SummaryTwoName:SummaryThreeName;
        var physical = index == 0 ? SummaryOnePhysical : index==1?SummaryTwoPhysical:SummaryThreePhysical;
        var render = index == 0 ? SummaryOneRender : index==1?SummaryTwoRender:SummaryThreeRender;
        var hz = index == 0 ? SummaryOneHz : index==1?SummaryTwoHz:SummaryThreeHz;
        name.Text = $"{index + 1} · {screen.Name}" + (index==_profile.PrimaryIndex?" · PRIMARY":"") + (index == 0 && _profile.Priority ? "   PRIORITY PRESET" : "");
        physical.Text = Dimensions(layout.VisibleBounds);
        render.Text = $"Render {Dimensions(layout.RenderWidth, layout.RenderHeight)}";
        hz.Text = $"{layout.RefreshHz} Hz target";
    }

    private void Edit(int index) { _invalidInput=false; ErrorText.Text=""; _selected = index; Render(); }
    private void EditOne_Click(object sender, RoutedEventArgs e) => Edit(0);
    private void EditTwo_Click(object sender, RoutedEventArgs e) => Edit(1);
    private void EditThree_Click(object sender,RoutedEventArgs e)=>Edit(2);
    private void SummaryOne_Click(object sender, RoutedEventArgs e) => Edit(0);
    private void SummaryTwo_Click(object sender, RoutedEventArgs e) => Edit(1);
    private void SummaryThree_Click(object sender,RoutedEventArgs e)=>Edit(2);
    private void PreviewOne_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Edit(0);
    private void PreviewTwo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Edit(1);
    private void PreviewThree_Click(object sender,System.Windows.Input.MouseButtonEventArgs e)=>Edit(2);

    private void ChangeScreen(Func<ScreenSettings, ScreenSettings> change)
    {
        if (_updating) return;
        _profile = _selected == 0 ? _profile with { First = change(_profile.First) } :
            _selected==1?_profile with { Second = change(_profile.Second) }:_profile with{Third=change(_profile.Third!)};
        Changed();
    }

    private void Changed()
    {
        _invalidInput = false;
        ErrorText.Text = "";
        Render();
        bool saved=false; try { if(!_previewOnly){_store.Save(_profile); saved=true;} }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ErrorText.Text = $"Could not save profile: {ex.Message}"; }
        if (!_session.IsActive)
        {
            StatusText.Text = "Local preview ready";
            StatusDetail.Text = _previewOnly?"Preview only. The saved profile and Windows display settings have not changed.":saved ? "Profile saved locally. Windows display settings have not changed." : "Preview updated, but profile could not be saved.";
        }
        if(_readinessCount!=_profile.Screens.Count)_=RefreshDriverReadiness();
    }

    private void MonitorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (_updating) return; _invalidInput=false; ErrorText.Text=""; _monitor = MonitorPicker.SelectedItem as MonitorInfo; Render(); }
    private void EqualButton_Click(object sender, RoutedEventArgs e)
    { _profile = Presets.Balanced(PanelLimit);_selected=Math.Min(_selected,1); Array.Fill(_customRatios, false); Changed(); }
    private void GameButton_Click(object sender, RoutedEventArgs e)
    { var preset = Presets.Gaming(PanelLimit); _profile = preset with { Second = preset.Second with { Name = "Discord", RefreshHz = Math.Min(30, PanelLimit) } };_selected=Math.Min(_selected,1); Array.Fill(_customRatios, false); Changed(); }
    private void PortraitButton_Click(object sender, RoutedEventArgs e)
    { var preset = Presets.Gaming(PanelLimit, true); _profile = preset with { Second = preset.Second with { Name = "Portrait", RefreshHz = Math.Min(30, PanelLimit) } };_selected=Math.Min(_selected,1); Array.Fill(_customRatios, false); Changed(); }
    private void SwapButton_Click(object sender, RoutedEventArgs e)
    { Reorder(Enumerable.Range(0,_profile.Screens.Count).Reverse().ToArray()); }
    private void MoveLeftButton_Click(object sender,RoutedEventArgs e)=>MoveSelected(-1);
    private void MoveRightButton_Click(object sender,RoutedEventArgs e)=>MoveSelected(1);
    private void MoveSelected(int delta){int destination=_selected+delta;if(destination<0||destination>=_profile.Screens.Count)return;var order=Enumerable.Range(0,_profile.Screens.Count).ToArray();(order[_selected],order[destination])=(order[destination],order[_selected]);Reorder(order);}
    private void Reorder(int[] order)
    {
        var custom=_customRatios.ToArray();int oldSelected=_selected;
        _profile=_profile.Reordered(order);
        for(int i=0;i<order.Length;i++)_customRatios[i]=custom[order[i]];_selected=Array.IndexOf(order,oldSelected);Changed();
    }
    private void MakePrimaryButton_Click(object sender,RoutedEventArgs e){_profile=_profile with{PrimaryIndex=_selected};Changed();}
    private void TwoScreensButton_Click(object sender,RoutedEventArgs e)
    {
        if(_profile.Third is null)return;int share=Math.Clamp((int)Math.Round(_profile.FirstShare*100d/(_profile.FirstShare+_profile.SecondShare)),15,85);
        _profile=_profile with{Third=null,SecondShare=0,FirstShare=share,PrimaryIndex=Math.Min(_profile.PrimaryIndex,1),Priority=false};_selected=Math.Min(_selected,1);_customRatios[2]=false;Changed();
    }
    private void ThreeScreensButton_Click(object sender,RoutedEventArgs e)
    {if(_profile.Third is not null)return;_profile=_profile with{Third=Presets.Triple(PanelLimit).Third,FirstShare=34,SecondShare=33,Priority=false};_customRatios[2]=false;Changed();}
    private void TripleButton_Click(object sender,RoutedEventArgs e){_profile=Presets.Triple(PanelLimit);_selected=1;Array.Fill(_customRatios,false);Changed();}
    private void ThreeShares_LostFocus(object sender,RoutedEventArgs e)
    {
        if(_updating||_profile.Third is null)return;
        if(!int.TryParse(FirstShareInput.Text,out int first)||!int.TryParse(SecondShareInput.Text,out int second)||first<1||second<1||(long)first+second>=100){InputError("The three shares must each leave at least 1%. Enter whole percentages for Screens 1 and 2.");return;}
        _profile=_profile with{FirstShare=first,SecondShare=second};Changed();
    }
    private int PanelLimit => Math.Max(1, (int)Math.Floor(_monitor?.RefreshHz ?? 60));
    private void ShareSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_updating) return; _profile = _profile with { FirstShare = (int)Math.Round(ShareSlider.Value) }; Changed(); }
    private void LandscapeButton_Click(object sender, RoutedEventArgs e) => ChangeScreen(s => s with { Portrait = false });
    private void PortraitOrientationButton_Click(object sender, RoutedEventArgs e) => ChangeScreen(s => s with { Portrait = true });
    private void NameInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var name = NameInput.Text.Trim();
        if (name.Length == 0) { _invalidInput = true; ErrorText.Text = "Screen name is required."; ApplyButton.IsEnabled = false; return; }
        if (name != SelectedScreen.Name) ChangeScreen(s => s with { Name = name }); else { _invalidInput=false; ErrorText.Text=""; Render(); }
    }
    private void RatioPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || RatioTag() is not { } tag) return;
        _customRatios[_selected] = tag == "Custom";
        ChangeScreen(s => tag switch
        {
            "Fill" => s with { Fill = true },
            "Custom" => s with { Fill = false },
            "16:9" => s with { AspectWidth = 16, AspectHeight = 9, Fill = false },
            "16:10" => s with { AspectWidth = 16, AspectHeight = 10, Fill = false },
            "21:9" => s with { AspectWidth = 21, AspectHeight = 9, Fill = false },
            _ => s
        });
    }
    private void CustomRatio_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (!int.TryParse(RatioWidthInput.Text, out var w) || w is < 1 or > 16384 ||
            !int.TryParse(RatioHeightInput.Text, out var h) || h is < 1 or > 16384)
        { InputError("Ratio values must be whole numbers from 1 to 16384.");return; }
        if (w != SelectedScreen.AspectWidth || h != SelectedScreen.AspectHeight)
            ChangeScreen(s => s with { AspectWidth = w, AspectHeight = h, Fill = false }); else { _invalidInput=false; ErrorText.Text=""; Render(); }
    }
    private void RenderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || RenderPicker.SelectedItem is not ComboBoxItem item) return;
        if(string.Equals(item.Tag?.ToString(),"Exact",StringComparison.Ordinal)){ChangeScreen(s=>s with{RenderShortEdge=0});return;}
        if(!int.TryParse(item.Tag?.ToString(),out var shortEdge))return;
        ChangeScreen(s => s with { RenderShortEdge = shortEdge,RenderWidth=0,RenderHeight=0 });
    }
    private void InputError(string message){_invalidInput=true;ErrorText.Text=message;ApplyButton.IsEnabled=false;}
    private void ExactDimensions_LostFocus(object sender,RoutedEventArgs e)
    {
        if(_updating)return;
        static bool Parse(string text,int maximum,out int value){if(string.IsNullOrWhiteSpace(text)){value=0;return true;}return int.TryParse(text,out value)&&value>=0&&value<=maximum;}
        if(!Parse(VisibleWidthInput.Text,16384,out int vw)||!Parse(VisibleHeightInput.Text,16384,out int vh)||!Parse(RenderWidthInput.Text,8192,out int rw)||!Parse(RenderHeightInput.Text,8192,out int rh)){InputError("Use whole pixels: visible 0–16384, render 0–8192. Zero means automatic.");return;}
        if(vw!=SelectedScreen.VisibleWidth||vh!=SelectedScreen.VisibleHeight||rw!=SelectedScreen.RenderWidth||rh!=SelectedScreen.RenderHeight)ChangeScreen(s=>s with{VisibleWidth=vw,VisibleHeight=vh,RenderWidth=rw,RenderHeight=rh,RenderShortEdge=rw>0||rh>0?0:s.RenderShortEdge});
        else{_invalidInput=false;ErrorText.Text="";Render();}
    }
    private void ExactRefresh_LostFocus(object sender,RoutedEventArgs e){if(_updating)return;if(!int.TryParse(ExactRefreshInput.Text,out int hz)||hz<1||hz>PanelLimit){InputError($"Enter a whole refresh target from 1 to {PanelLimit} Hz.");return;}if(hz!=SelectedScreen.RefreshHz)ChangeScreen(s=>s with{RefreshHz=hz});else{_invalidInput=false;ErrorText.Text="";Render();}}
    private void RefreshPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || RefreshPicker.SelectedItem is not ComboBoxItem item || item.Tag is not int hz) return;
        ChangeScreen(s => s with { RefreshHz = hz });
    }
    private void PriorityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var enabled = PriorityToggle.IsChecked == true;
        _profile = _profile with
        {
            Priority = enabled, FirstShare = enabled ? 70 : 50,
            First = _profile.First with { RefreshHz = enabled ? PanelLimit : Math.Min(60, PanelLimit) },
            Second = _profile.Second with { RefreshHz = enabled ? Math.Min(30, PanelLimit) : Math.Min(60, PanelLimit) }
        };
        Changed();
    }
    private void SizeInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updating || _monitor is null) return;
        if (string.IsNullOrWhiteSpace(SizeInput.Text)) { _manualSizes.Remove(_monitor.DeviceName); _invalidInput=false; ErrorText.Text = ""; Render(); return; }
        if (!double.TryParse(SizeInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var inches) ||
            !double.IsFinite(inches) || inches is < 10 or > 100)
        { _invalidInput = true; ErrorText.Text = "Enter a display size from 10 to 100 inches, or leave it blank."; ApplyButton.IsEnabled = false; return; }
        _manualSizes[_monitor.DeviceName] = inches;
        _invalidInput = false; ErrorText.Text = ""; Render();
    }

    private bool IsEqualPreset() => _profile == Presets.Balanced(PanelLimit);
    private bool IsGamingPreset(bool portrait) => _profile.Third is null&&_profile.FirstShare == 70 && _profile.Priority &&
        _profile.First.Portrait == false && _profile.Second.Portrait == portrait &&
        _profile.First.RefreshHz == PanelLimit && _profile.Second.RefreshHz == Math.Min(30, PanelLimit);

    private async void SetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy||_previewOnly) return;
        if (RestartForAdministrator("setup")) return;
        await RunOperation(async () =>
        {
            StatusText.Text = "Setting up virtual display driver…";
            await _session.SetupDriverAsync(_profile.Screens.Count);
            StatusText.Text = "Driver ready";
            StatusDetail.Text = "The driver is installed. Review the layout, then Apply explicitly.";
        });
    }
    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewOnly||_busy || _invalidInput || _monitor is null || _layout is null) return;
        if (!_session.IsDriverInstalled)
        { ErrorText.Text = "Set up the virtual display driver before applying this layout."; return; }
        if (RestartForAdministrator("apply")) return;
        var monitor = _monitor;
        var profile = _profile;
        await RunOperation(async () =>
        {
            StatusText.Text = "Applying virtual displays…";
            StatusDetail.Text = $"Waiting for {_profile.Screens.Count} virtual outputs and presentation.";
            await _session.ApplyAsync(monitor, profile);
            StatusText.Text = "Virtual display session active";
            StatusDetail.Text = _session.CaptureSummary+" Closing SplitDeck also restores the desktop.";
        });
    }
    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_session.IsActive) return;
        await RunOperation(async () =>
        {
            StatusText.Text = "Restoring desktop…";
            await _session.RestoreAsync();
            StatusText.Text = "Desktop restored";
            StatusDetail.Text = "Editing is local until Apply.";
        });
    }
    private async Task RunOperation(Func<Task> operation)
    {
        _busy = true;
        ApplyButton.IsEnabled = SetupButton.IsEnabled = RestoreButton.IsEnabled = false;
        ErrorText.Text = "";
        try { await operation(); }
        catch (Exception ex)
        {
            ShowOperationError(ex);
        }
        finally
        {
            // Driver verification is background maintenance after the operation.
            // Keep Restore/close available while readiness alone disables Apply.
            _busy = false; SetupButton.IsEnabled = true;
            await RefreshDriverReadiness();
        }
    }

    private async Task RefreshDriverReadiness()
    {
        _readinessCount=_profile.Screens.Count;
        Task refresh = _session.RefreshDriverReadinessAsync(_readinessCount);
        Render(preserveDrafts:true); // Readiness changes must not replace an in-progress edit.
        await refresh;
        if (_closingAfterRestore) return;
        Render(preserveDrafts:true);
        if (!_busy && !_session.IsActive && _monitor is not null)
        {
            StatusDetail.Text = _session.DriverReadinessError is { } error
                ? $"Driver verification failed: {error}. Setup is available; Apply is disabled."
                : _session.IsDriverInstalled
                    ? "Editing is local until Apply. The installed driver is ready."
                    : "Driver setup is required before Apply. Editing is local until Apply.";
        }
    }

    private bool RestartForAdministrator(string action)
    {
        if (EditorElevation.IsElevated) return false;
        try
        {
            StatusText.Text = "Opening Windows administrator prompt…";
            EditorElevation.Restart(new(_profile, _monitor?.DeviceName, action));
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { ShowOperationError(new InvalidOperationException("Administrator permission was canceled. No driver or display settings were changed. Click Setup driver again when you are ready to approve Windows’ prompt.")); }
        catch (Exception ex) { ShowOperationError(ex); }
        return true;
    }

    public async void ResumeElevatedRequest(EditorLaunchRequest request)
    {
        if (!EditorElevation.IsElevated)
        { ShowOperationError(new InvalidOperationException("Windows did not grant administrator permission. Setup was not started.")); return; }
        _profile = request.Profile;
        _customRatios[0] = IsCustomRatio(_profile.First);
        _customRatios[1] = IsCustomRatio(_profile.Second);
        if(_profile.Third is not null)_customRatios[2]=IsCustomRatio(_profile.Third);
        var monitor = MonitorPicker.Items.OfType<MonitorInfo>().FirstOrDefault(x => x.DeviceName == request.MonitorDeviceName);
        if (monitor is not null)
        {
            _updating = true; MonitorPicker.SelectedItem = monitor; _monitor = monitor; _updating = false;
        }
        _invalidInput = false;
        Render();
        await RefreshDriverReadiness();
        if (request.Action == "setup") SetupButton_Click(this, new RoutedEventArgs());
        else if (monitor is null || _layout is null)
            ShowOperationError(new InvalidOperationException("The selected monitor or layout is no longer available. Review the settings before applying."));
        else ApplyButton_Click(this, new RoutedEventArgs());
    }

    private void ShowOperationError(Exception error)
    {
        ErrorText.Text = error.Message;
        StatusText.Text = "Action failed";
        StatusDetail.Text = error.Message;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitDeck");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "operations.log"), $"{DateTimeOffset.Now:O} {error}\n");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
        MessageBox.Show(this, error.Message, "SplitDeck — action failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private void OnSessionStatus(string status)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnSessionStatus(status)); return; }
        StatusText.Text = status;
    }
    private async void OnSessionEnded()
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(OnSessionEnded); return; }

        await RefreshDriverReadiness();
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterRestore) return;
        if (_busy)
        { e.Cancel = true; ErrorText.Text = "Wait for the current display operation to finish before closing."; return; }
        e.Cancel = true;
        _closingAfterRestore = true;
        IsEnabled = false;
        try
        {
            await _session.DisposeAsync();
            _tray?.Dispose();_tray=null;
            // Exit the current Closing event before initiating the final close.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            _closingAfterRestore = false;
            IsEnabled = true;
            ErrorText.Text = $"Could not restore before closing: {ex.Message}";
            StatusText.Text = "Restore needs attention";
        }
    }
}

