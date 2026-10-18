using SplitDeck.Core;

var tests = new (string Name, Action Run)[]
{
    ("divider dragging transfers only neighboring shares and preserves screen settings", DividerNeighbors),
    ("divider dragging clamps each region and rejects invalid movement", DividerLimits),
    ("layout demand rises with refresh while buffer memory stays fixed", DemandRefresh),
    ("layout demand rises with render resolution and handles three screens", DemandResolution),
    ("layout demand rejects invalid input and preserves fixed reference", DemandValidation),
    ("balanced visible rectangles preserve exact 16:9", BalancedGeometry),
    ("priority gets a 70 percent first column", PriorityGeometry),
    ("portrait aspect fits the second column", PortraitGeometry),
    ("landscape orientation rotates a 9:16 ratio", LandscapeFromPortraitRatio),
    ("visible rectangles never overlap", NonOverlap),
    ("render short edge changes virtual resolution only", VirtualRender),
    ("refresh is clamped to panel floor", RefreshClamp),
    ("invalid inputs are rejected", InvalidInputs),
    ("preset values match the design", PresetValues),
    ("profile store roundtrips and replaces", ProfileRoundtrip),
    ("invalid JSON is retained", InvalidJson)
    ,("three screen preset preserves portrait wings center primary and panel cap", TripleGeometry)
    ,("explicit visible sizes reserve columns and auto screens share remaining width", ExactVisible)
    ,("explicit render dimensions retain visible geometry and reject stretch", ExactRender)
    ,("three automatic columns stay disjoint across share combinations", TripleNonOverlap)
    ,("legacy JSON constructors remain compatible and Screens is not serialized", LegacyProfiles)
    ,("conflicting exact dimensions and invalid primary shares are rejected", ExactValidation)
    ,("mixed exact and automatic triple columns reserve pixels before weighting", MixedExactTriple)
    ,("explicit free-ratio dimensions and fully fixed groups remain bounded", FixedAndFill)
    ,("reordering preserves settings shares and the selected primary identity", ReorderIdentity)
    ,("explicit physical output refresh is opt-in bounded and never lowers virtual requests", OutputRefresh)
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static ScreenSettings Screen(string name = "Screen", int aw = 16, int ah = 9, bool portrait = false, bool fill = false, int render = 0, int refresh = 60)
    => new(name, aw, ah, portrait, fill, render, refresh);
static LayoutProfile Profile(int share = 50, ScreenSettings? second = null)
    => new(Screen("First"), second ?? Screen("Second"), share, share != 50);
static void Eq<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
static void Throws(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected ArgumentException");
}
static void BalancedGeometry()
{
    var screens = LayoutEngine.Calculate(Profile(), 2560, 1080, 60);
    Eq(2, screens.Count);
    Eq(new PixelRect(0, 180, 1280, 720), screens[0].VisibleBounds);
    Eq(new PixelRect(1280, 180, 1280, 720), screens[1].VisibleBounds);
    Eq(0, screens[0].Index);
    Eq(1, screens[1].Index);
}
static void PriorityGeometry()
{
    var screens = LayoutEngine.Calculate(Profile(70), 2560, 1080, 60);
    Eq(new PixelRect(0, 36, 1792, 1008), screens[0].VisibleBounds);
    Eq(new PixelRect(1280 + 512 + 0, 324, 768, 432), screens[1].VisibleBounds);
}
static void PortraitGeometry()
{
    var screens = LayoutEngine.Calculate(Profile(70, Screen("Portrait", portrait: true)), 2560, 1080, 60);
    Eq(new PixelRect(1874, 4, 603, 1072), screens[1].VisibleBounds);
}
static void LandscapeFromPortraitRatio()
{
    var screens = LayoutEngine.Calculate(Profile(70, Screen("Landscape", aw: 9, ah: 16)), 2560, 1080, 60);
    Eq(new PixelRect(1792, 324, 768, 432), screens[1].VisibleBounds);
}
static void NonOverlap()
{
    for (int share = 15; share <= 85; share++)
    {
        var screens = LayoutEngine.Calculate(Profile(share, Screen("B", aw: 21, ah: 9)), 3440, 1440, 120);
        var a = screens[0].VisibleBounds;
        var b = screens[1].VisibleBounds;
        if (a.X + a.Width > b.X) throw new Exception($"Overlap at share {share}");
        if (a.X < 0 || b.X + b.Width > 3440) throw new Exception($"Out of bounds at share {share}");
    }
}
static void VirtualRender()
{
    var p = Profile(second: Screen("Second", render: 2160));
    var screens = LayoutEngine.Calculate(p, 2560, 1080, 60);
    Eq(new PixelRect(1280, 180, 1280, 720), screens[1].VisibleBounds);
    Eq(3840, screens[1].RenderWidth);
    Eq(2160, screens[1].RenderHeight);
    Eq(1280, screens[0].RenderWidth);
    Eq(720, screens[0].RenderHeight);
}
static void RefreshClamp()
{
    var p = new LayoutProfile(Screen("A", refresh: 240), Screen("B", refresh: 30), 50, false);
    var screens = LayoutEngine.Calculate(p, 2560, 1080, 143.9);
    Eq(143, screens[0].RefreshHz);
    Eq(30, screens[1].RefreshHz);
}
static void InvalidInputs()
{
    var p = Profile();
    Throws(() => LayoutEngine.Calculate(p, 0, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p, 16385, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p, 2560, 1080, double.NaN));
    Throws(() => LayoutEngine.Calculate(p with { FirstShare = 14 }, 2560, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p with { First = Screen(" ") }, 2560, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p with { First = Screen("A", aw: 0) }, 2560, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p with { First = Screen("A", render: 239) }, 2560, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p with { First = Screen("A", render: 4321) }, 2560, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p with { First = Screen("A", refresh: 0) }, 2560, 1080, 60));
    Throws(() => LayoutEngine.Calculate(p, 2560, 1080, 0));
    Throws(() => LayoutEngine.Calculate(new LayoutProfile(Screen("A", aw: 1, ah: 100, render: 4320), Screen("B"), 50, false), 2560, 1080, 60));
}
static void PresetValues()
{
    var balanced = Presets.Balanced(75);
    Eq(50, balanced.FirstShare);
    Eq(16, balanced.First.AspectWidth);
    Eq(9, balanced.Second.AspectHeight);
    Eq(60, balanced.First.RefreshHz);
    var gaming = Presets.Gaming(143, portrait: true);
    Eq(70, gaming.FirstShare);
    Eq(143, gaming.First.RefreshHz);
    Eq(30, gaming.Second.RefreshHz);
    Eq(true, gaming.Second.Portrait);
}
static void ProfileRoundtrip()
{
    var dir = Path.Combine(Path.GetTempPath(), "SplitDeck.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProfileStore(dir);
        Eq<LayoutProfile?>(null, store.Load());
        store.Save(Profile());
        Eq(Profile(), store.Load());
        var newer = Profile(70);
        store.Save(newer);
        Eq(newer, store.Load());
        Eq(1, Directory.GetFiles(dir, "profile.json").Length);
        Eq(0, Directory.GetFiles(dir, "*.tmp").Length);
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
static void InvalidJson()
{
    var dir = Path.Combine(Path.GetTempPath(), "SplitDeck.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "profile.json"), "{ bad json");
        Eq<LayoutProfile?>(null, new ProfileStore(dir).Load());
        var retained = File.Exists(Path.Combine(dir, "profile.json")) || Directory.GetFiles(dir, "*.invalid*").Length > 0;
        Eq(true, retained);
    }
    finally { Directory.Delete(dir, true); }
}
static void TripleGeometry()
{
 var profile=Presets.Triple(45);Eq(3,profile.Screens.Count);Eq(1,profile.PrimaryIndex);Eq(19,profile.FirstShare);Eq(62,profile.SecondShare);
 var layout=LayoutEngine.Calculate(profile,3440,1440,45);Eq(3,layout.Count);Eq(30,layout[0].RefreshHz);Eq(45,layout[1].RefreshHz);Eq(30,layout[2].RefreshHz);
 Eq((long)9*layout[0].VisibleBounds.Height,(long)16*layout[0].VisibleBounds.Width);Eq((long)16*layout[1].VisibleBounds.Height,(long)9*layout[1].VisibleBounds.Width);
 Eq(true,layout[2].VisibleBounds.X>=layout[1].VisibleBounds.X+layout[1].VisibleBounds.Width);
}
static void ExactVisible()
{
 var first=Screen("Portrait",portrait:true) with{VisibleWidth=585,VisibleHeight=1040};
 var p=new LayoutProfile(first,Screen("Main"),19,false);
 var layout=LayoutEngine.Calculate(p,2560,1080,60);Eq(new PixelRect(0,20,585,1040),layout[0].VisibleBounds);Eq(new PixelRect(612,0,1920,1080),layout[1].VisibleBounds);
 var viaHeight=LayoutEngine.Calculate(p with{First=first with{VisibleWidth=0}},2560,1080,60);Eq(layout[0].VisibleBounds,viaHeight[0].VisibleBounds);
}
static void ExactRender()
{
 var p=Profile() with{First=Screen("First") with{RenderWidth=1920,RenderHeight=1080}};
 var layout=LayoutEngine.Calculate(p,2560,1080,60);Eq(new PixelRect(0,180,1280,720),layout[0].VisibleBounds);Eq(1920,layout[0].RenderWidth);Eq(1080,layout[0].RenderHeight);
 var derived=LayoutEngine.Calculate(p with{First=p.First with{RenderHeight=0}},2560,1080,60);Eq(1080,derived[0].RenderHeight);
 Throws(()=>LayoutEngine.Calculate(p with{First=p.First with{RenderHeight=1200}},2560,1080,60));
}
static void TripleNonOverlap()
{
 for(int first=10;first<=30;first+=5)for(int second=30;second<=60;second+=5){
  var p=new LayoutProfile(Screen("A",portrait:true),Screen("B"),first,false,Screen("C",portrait:true),second,1);var layout=LayoutEngine.Calculate(p,3440,1440,60);
  Eq(3,layout.Count);for(int i=0;i<layout.Count;i++){var b=layout[i].VisibleBounds;Eq(true,b.Width>0&&b.Height>0&&b.X>=0&&b.Y>=0&&b.X+b.Width<=3440&&b.Y+b.Height<=1440);if(i>0)Eq(true,layout[i-1].VisibleBounds.X+layout[i-1].VisibleBounds.Width<=b.X);}
 }
}
static void LegacyProfiles()
{
 const string legacy="{\"First\":{\"Name\":\"A\",\"AspectWidth\":16,\"AspectHeight\":9,\"Portrait\":false,\"Fill\":false,\"RenderShortEdge\":0,\"RefreshHz\":60},\"Second\":{\"Name\":\"B\",\"AspectWidth\":16,\"AspectHeight\":9,\"Portrait\":false,\"Fill\":false,\"RenderShortEdge\":0,\"RefreshHz\":30},\"FirstShare\":50,\"Priority\":false}";
 var p=System.Text.Json.JsonSerializer.Deserialize<LayoutProfile>(legacy)!;Eq(2,p.Screens.Count);Eq(0,p.PrimaryIndex);Eq(0,p.First.VisibleWidth);Eq(2,LayoutEngine.Calculate(p,2560,1080,60).Count);
 var three=Presets.Triple(144) with{Third=Screen("Exact") with{VisibleWidth=320,VisibleHeight=180,RenderWidth=640,RenderHeight=360}};string json=System.Text.Json.JsonSerializer.Serialize(three);Eq(false,json.Contains("\"Screens\"",StringComparison.Ordinal));Eq(three,System.Text.Json.JsonSerializer.Deserialize<LayoutProfile>(json));
}
static void ExactValidation()
{
 var p=Profile();Throws(()=>LayoutEngine.Calculate(p with{PrimaryIndex=2},2560,1080,60));
 Throws(()=>LayoutEngine.Calculate(p with{First=p.First with{VisibleWidth=1920,VisibleHeight=1000}},2560,1080,60));
 Throws(()=>LayoutEngine.Calculate(p with{First=p.First with{VisibleWidth=3000,VisibleHeight=1687}},2560,1080,60));
 Throws(()=>LayoutEngine.Calculate(p with{First=p.First with{VisibleHeight=721}},2560,1080,60));
 Throws(()=>LayoutEngine.Calculate(p with{First=p.First with{RenderWidth=-1}},2560,1080,60));
 Throws(()=>LayoutEngine.Calculate(p with{First=p.First with{RenderWidth=1920,RenderShortEdge=1080}},2560,1080,60));
 Throws(()=>LayoutEngine.Calculate(p with{Third=Screen("C"),SecondShare=50,FirstShare=50},2560,1080,60));
}
static void MixedExactTriple()
{
 var p=Presets.Triple(60) with{First=Screen("Left",portrait:true) with{VisibleWidth=360,VisibleHeight=640},FirstShare=19,SecondShare=62};
 var layout=LayoutEngine.Calculate(p,2560,1080,60);Eq(new PixelRect(0,220,360,640),layout[0].VisibleBounds);
 Eq(true,layout[1].VisibleBounds.X>=360&&layout[1].VisibleBounds.X+layout[1].VisibleBounds.Width<=layout[2].VisibleBounds.X);
 Eq(true,layout[2].VisibleBounds.X+layout[2].VisibleBounds.Width<=2560);
 var h=Profile() with{First=Screen("Height",fill:true) with{VisibleHeight=500}};Eq(new PixelRect(0,290,1280,500),LayoutEngine.Calculate(h,2560,1080,60)[0].VisibleBounds);
}
static void FixedAndFill()
{
 var a=Screen("A",fill:true) with{VisibleWidth=800,VisibleHeight=600};var b=Screen("B",fill:true) with{VisibleWidth=800,VisibleHeight=600};
 var layout=LayoutEngine.Calculate(new(a,b,50,false),2560,1080,60);Eq(new PixelRect(480,240,800,600),layout[0].VisibleBounds);Eq(new PixelRect(1280,240,800,600),layout[1].VisibleBounds);
 Throws(()=>LayoutEngine.Calculate(new(a with{VisibleWidth=2000},b with{VisibleWidth=2000},50,false),2560,1080,60));
}
static void ReorderIdentity()
{
 var p=Presets.Triple(144);var moved=p.Reordered([1,0,2]);Eq(p.Second,moved.First);Eq(62,moved.FirstShare);Eq(19,moved.SecondShare);Eq(0,moved.PrimaryIndex);
 var reverse=p.Reordered([2,1,0]);Eq(1,reverse.PrimaryIndex);Eq(p.Third,reverse.First);Eq(p.First,reverse.Third);
 var two=Presets.Gaming(144) with{PrimaryIndex=1};var swapped=two.Reordered([1,0]);Eq(30,swapped.FirstShare);Eq(0,swapped.PrimaryIndex);Eq<ScreenSettings?>(null,swapped.Third);
 Throws(()=>p.Reordered([0,0,2]));Throws(()=>p.Reordered([0,1]));
}
static void OutputRefresh()
{
 var p=Presets.Balanced(144);Eq(0,p.OutputRefreshHz);var defaults=LayoutEngine.Calculate(p,2560,1080,144);
 var explicit60=p with{OutputRefreshHz=60};var result=LayoutEngine.Calculate(explicit60,2560,1080,144);Eq(defaults[0],result[0]);Eq(defaults[1],result[1]);
 Eq(60,explicit60.Reordered([1,0]).OutputRefreshHz);
 Throws(()=>LayoutEngine.Calculate(p with{OutputRefreshHz=-1},2560,1080,144));
 Throws(()=>LayoutEngine.Calculate(p with{OutputRefreshHz=1001},2560,1080,144));
 Throws(()=>LayoutEngine.Calculate(p with{OutputRefreshHz=145},2560,1080,144));
 Throws(()=>LayoutEngine.Calculate(p with{OutputRefreshHz=30},2560,1080,144));
 var fast=Presets.Gaming(144);Throws(()=>LayoutEngine.Calculate(fast with{OutputRefreshHz=60},2560,1080,144));
 Eq(144,LayoutEngine.Calculate(fast with{OutputRefreshHz=144},2560,1080,144)[0].RefreshHz);
}

static void DemandRefresh(){
 VirtualScreenLayout[] s=[new(0,"A",new(0,0,1280,1080),1280,1080,144),new(1,"B",new(1280,0,1280,1080),1280,1080,30)];
 var low=LayoutDemandEstimator.Estimate(s,2560,1080,144);var mid=LayoutDemandEstimator.Estimate([s[0],s[1] with{RefreshHz=60}],2560,1080,144);var high=LayoutDemandEstimator.Estimate([s[0],s[1] with{RefreshHz=144}],2560,1080,144);
 if(!(low.WorkloadRatio<mid.WorkloadRatio&&mid.WorkloadRatio<high.WorkloadRatio))throw new Exception("144/30 < 144/60 < 144/144 must hold.");
 if(low.BufferMiB!=high.BufferMiB||Math.Abs(high.WorkloadRatio-1)>1e-9)throw new Exception("Hz must not inflate memory; native144/144 reference must equal1.");
}
static void DemandResolution(){
 var s=LayoutEngine.Calculate(Presets.Triple(144),2560,1080,144);var a=LayoutDemandEstimator.Estimate(s,2560,1080,144);var bigger=s.Select(x=>x with{RenderWidth=x.RenderWidth*2,RenderHeight=x.RenderHeight*2}).ToArray();var b=LayoutDemandEstimator.Estimate(bigger,2560,1080,144);
 if(b.WorkloadRatio<=a.WorkloadRatio||b.BufferMiB<=a.BufferMiB)throw new Exception("Higher render pixels must raise both estimates.");
 var reordered=LayoutDemandEstimator.Estimate(s.Reverse().ToArray(),2560,1080,144);if(reordered!=a)throw new Exception("Order alone must not change demand.");
}
static void DemandValidation(){
 VirtualScreenLayout[] s=[new(0,"A",new(0,0,1280,1080),1280,1080,60),new(1,"B",new(1280,0,1280,1080),1280,1080,60)];
 Throws(()=>LayoutDemandEstimator.Estimate(s,0,1080,144));Throws(()=>LayoutDemandEstimator.Estimate(s,2560,1080,double.NaN));Throws(()=>LayoutDemandEstimator.Estimate([s[0] with{RefreshHz=0},s[1]],2560,1080,144));Throws(()=>LayoutDemandEstimator.Estimate([],2560,1080,144));
 var a=LayoutDemandEstimator.Estimate(s,2560,1080,144);if(Math.Abs(a.BufferRatio-1)>1e-9||a.WorkloadRatio>=1)throw new Exception("Reference must stay fixed while changing refresh.");
}

static void DividerNeighbors(){
 var p=Presets.Triple(144);var left=ColumnResize.Move(p,0,10);Eq(29,left.FirstShare);Eq(52,left.SecondShare);Eq(19,100-left.FirstShare-left.SecondShare);
 var right=ColumnResize.Move(p,1,10);Eq(19,right.FirstShare);Eq(72,right.SecondShare);Eq(9,100-right.FirstShare-right.SecondShare);
 if(left.First!=p.First||left.Second!=p.Second||left.Third!=p.Third||left.PrimaryIndex!=p.PrimaryIndex||left.OutputRefreshHz!=p.OutputRefreshHz)throw new Exception("Dragging must preserve per-screen configuration and primary.");
 Eq(20,ColumnResize.Move(p,0,.6).FirstShare);Eq(19,ColumnResize.Move(p,0,.4).FirstShare);
}
static void DividerLimits(){
 var p=Presets.Triple(144);var a=ColumnResize.Move(p,0,-10000);Eq(1,a.FirstShare);Eq(80,a.SecondShare);var b=ColumnResize.Move(p,1,10000);Eq(80,b.SecondShare);Eq(1,100-b.FirstShare-b.SecondShare);
 Throws(()=>ColumnResize.Move(p,2,5));Throws(()=>ColumnResize.Move(p,0,double.NaN));Throws(()=>ColumnResize.Move(Presets.Balanced(144),0,5));
 foreach(int index in new[]{0,1})foreach(double delta in new[]{-100d,-50d,0d,50d,100d}){var x=ColumnResize.Move(p,index,delta);if(x.FirstShare<1||x.SecondShare<1||x.FirstShare+x.SecondShare>=100)throw new Exception("Invalid share after drag.");LayoutEngine.Calculate(x,2560,1080,144);}
}
