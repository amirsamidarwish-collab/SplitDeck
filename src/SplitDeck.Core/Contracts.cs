namespace SplitDeck.Core;

public sealed record MonitorInfo(string DeviceName, string FriendlyName, int X, int Y, int Width, int Height, double RefreshHz, double? DiagonalInches, bool IsPrimary, bool IsVirtual);
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
public sealed record ScreenSettings(string Name, int AspectWidth, int AspectHeight, bool Portrait, bool Fill, int RenderShortEdge, int RefreshHz,int VisibleWidth=0,int VisibleHeight=0,int RenderWidth=0,int RenderHeight=0);
public sealed record LayoutProfile(ScreenSettings First, ScreenSettings Second, int FirstShare, bool Priority,ScreenSettings? Third=null,int SecondShare=0,int PrimaryIndex=0,int OutputRefreshHz=0)
{
 [System.Text.Json.Serialization.JsonIgnore]public IReadOnlyList<ScreenSettings> Screens=>Third is null?[First,Second]:[First,Second,Third];
 public LayoutProfile Reordered(IReadOnlyList<int> order)
 {
  var screens=Screens;if(order.Count!=screens.Count||!order.Order().SequenceEqual(Enumerable.Range(0,screens.Count)))throw new ArgumentException("The order must contain each screen index exactly once.",nameof(order));
  int[] shares=screens.Count==2?[FirstShare,100-FirstShare]:[FirstShare,SecondShare,100-FirstShare-SecondShare];
  int primary=Enumerable.Range(0,order.Count).Single(i=>order[i]==PrimaryIndex);
  return new(screens[order[0]],screens[order[1]],shares[order[0]],false,screens.Count==3?screens[order[2]]:null,screens.Count==3?shares[order[1]]:0,primary,OutputRefreshHz);
 }
}
public sealed record VirtualScreenLayout(int Index, string Name, PixelRect VisibleBounds, int RenderWidth, int RenderHeight, int RefreshHz);

public static class LayoutEngine
{
    public static IReadOnlyList<VirtualScreenLayout> Calculate(LayoutProfile profile, int width, int height, double panelHz)
    {
        ValidateProfile(profile);
        if (width is < 1 or > 16384 || height is < 1 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(width), "Monitor bounds must be 1..16384 pixels.");
        if (!double.IsFinite(panelHz) || panelHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(panelHz));

        var screens=profile.Screens;
        int[] weights=screens.Count==2?[profile.FirstShare,100-profile.FirstShare]:[profile.FirstShare,profile.SecondShare,100-profile.FirstShare-profile.SecondShare];
        var budgets=screens.Select(ExplicitWidth).ToArray();
        int remaining=width-budgets.Sum();if(remaining<0)throw new ArgumentException("The exact visible widths exceed the physical monitor width.",nameof(profile));
        var automatic=Enumerable.Range(0,screens.Count).Where(i=>budgets[i]==0).ToArray();
        if(automatic.Length>0){
            if(remaining<automatic.Length)throw new ArgumentException("No space remains for the automatic screens.",nameof(profile));
            int totalWeight=automatic.Sum(i=>weights[i]),allocated=0;
            for(int slot=0;slot<automatic.Length;slot++){int index=automatic[slot];budgets[index]=slot==automatic.Length-1?remaining-allocated:(int)((long)remaining*weights[index]/totalWeight);allocated+=budgets[index];if(budgets[index]<1)throw new ArgumentException("An automatic screen has no visible width. Increase its share.",nameof(profile));}
        }
        var panelLimit = (int)Math.Clamp(Math.Floor(panelHz), 1, int.MaxValue);
        var result=new List<VirtualScreenLayout>();int columnX=automatic.Length==0?remaining/2:0;
        for(int index=0;index<screens.Count;index++){result.Add(Build(screens[index],index,columnX,budgets[index],height,panelLimit));columnX+=budgets[index];}
        if(profile.OutputRefreshHz>0&&(profile.OutputRefreshHz>panelHz||profile.OutputRefreshHz<result.Max(screen=>screen.RefreshHz)))throw new ArgumentException("An explicit physical output refresh must not exceed the current panel rate or fall below any virtual screen's actual refresh target.",nameof(profile));
        return result;
    }

    internal static void ValidateProfile(LayoutProfile? profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if(profile.Third is null){if(profile.FirstShare is <15 or >85)throw new ArgumentOutOfRangeException(nameof(profile.FirstShare));}
        else if(profile.FirstShare<1||profile.SecondShare<1||(long)profile.FirstShare+profile.SecondShare>=100)throw new ArgumentException("Three screen shares must each reserve at least one percent.",nameof(profile));
        if(profile.PrimaryIndex<0||profile.PrimaryIndex>=profile.Screens.Count)throw new ArgumentOutOfRangeException(nameof(profile.PrimaryIndex));
        if(profile.OutputRefreshHz is <0 or >1000)throw new ArgumentOutOfRangeException(nameof(profile.OutputRefreshHz),"Physical output refresh must be zero (unchanged) or 1..1000 Hz.");
        foreach(var screen in profile.Screens)ValidateScreen(screen);
    }

    private static void ValidateScreen(ScreenSettings? screen)
    {
        if (screen is null) throw new ArgumentNullException(nameof(screen));
        if (string.IsNullOrWhiteSpace(screen.Name)) throw new ArgumentException("Screen name is required.", nameof(screen));
        if (screen.AspectWidth <= 0 || screen.AspectHeight <= 0)
            throw new ArgumentException("Aspect ratio must be positive.", nameof(screen));
        if (screen.RenderShortEdge != 0 && screen.RenderShortEdge is < 240 or > 4320)
            throw new ArgumentOutOfRangeException(nameof(screen.RenderShortEdge));
        if (screen.RefreshHz <= 0) throw new ArgumentOutOfRangeException(nameof(screen.RefreshHz));
        if(screen.VisibleWidth is <0 or >16384||screen.VisibleHeight is <0 or >16384)throw new ArgumentException("Visible dimensions must be zero (automatic) or 1..16384 pixels.",nameof(screen));
        if(screen.RenderWidth is <0 or >8192||screen.RenderHeight is <0 or >8192)throw new ArgumentException("Render dimensions must be zero (automatic) or 1..8192 pixels.",nameof(screen));
        if(screen.RenderShortEdge>0&&(screen.RenderWidth>0||screen.RenderHeight>0))throw new ArgumentException("Choose either a render-detail preset or exact render dimensions, not both.",nameof(screen));
    }

    private static VirtualScreenLayout Build(ScreenSettings screen, int index, int columnX, int budget, int height, int panelLimit)
    {
        int visibleWidth;
        int visibleHeight;
        var (ratioWidth,ratioHeight)=Ratio(screen);
        if(screen.VisibleWidth>0||screen.VisibleHeight>0)
        {
            visibleWidth=screen.VisibleWidth>0?screen.VisibleWidth:screen.Fill?budget:ExactOther(screen.VisibleHeight,ratioWidth,ratioHeight,"visible height");
            visibleHeight=screen.VisibleHeight>0?screen.VisibleHeight:screen.Fill?height:ExactOther(screen.VisibleWidth,ratioHeight,ratioWidth,"visible width");
            if(visibleWidth>budget||visibleHeight>height)throw new ArgumentException($"Screen {index+1}'s exact visible dimensions do not fit the panel.",nameof(screen));
            if(!screen.Fill&&(long)visibleWidth*ratioHeight!=(long)visibleHeight*ratioWidth)throw new ArgumentException($"Screen {index+1}'s exact visible dimensions conflict with its aspect ratio.",nameof(screen));
        }
        else if (screen.Fill)
        {
            visibleWidth = budget;
            visibleHeight = height;
        }
        else
        {
            var scale = Math.Min(budget / ratioWidth, height / ratioHeight);
            if (scale < 1) throw new ArgumentException("Aspect ratio cannot fit in the column.", nameof(screen));
            visibleWidth = ratioWidth * scale;
            visibleHeight = ratioHeight * scale;
        }

        var bounds = new PixelRect(columnX + (budget - visibleWidth) / 2,
                                   (height - visibleHeight) / 2, visibleWidth, visibleHeight);
        var renderWidth = visibleWidth;
        var renderHeight = visibleHeight;
        if(screen.RenderWidth>0||screen.RenderHeight>0)
        {
            renderWidth=screen.RenderWidth>0?screen.RenderWidth:ExactOther(screen.RenderHeight,visibleWidth,visibleHeight,"render height");
            renderHeight=screen.RenderHeight>0?screen.RenderHeight:ExactOther(screen.RenderWidth,visibleHeight,visibleWidth,"render width");
            if((long)renderWidth*visibleHeight!=(long)renderHeight*visibleWidth)throw new ArgumentException($"Screen {index+1}'s exact render dimensions would stretch its visible aspect ratio.",nameof(screen));
        }
        else if (screen.RenderShortEdge > 0)
        {
            if (visibleWidth <= visibleHeight)
            {
                renderWidth = NearestEven(screen.RenderShortEdge);
                renderHeight = NearestEven((double)screen.RenderShortEdge * visibleHeight / visibleWidth);
            }
            else
            {
                renderHeight = NearestEven(screen.RenderShortEdge);
                renderWidth = NearestEven((double)screen.RenderShortEdge * visibleWidth / visibleHeight);
            }
        }
        if (renderWidth is < 1 or > 8192 || renderHeight is < 1 or > 8192)
            throw new ArgumentException("Virtual rendering exceeds 8192 pixels on an edge.", nameof(screen));
        return new VirtualScreenLayout(index, screen.Name, bounds, renderWidth, renderHeight,
                                       Math.Max(1, Math.Min(screen.RefreshHz, panelLimit)));
    }

    private static int NearestEven(double value) => checked((int)(2 * Math.Round(value / 2, MidpointRounding.AwayFromZero)));
    private static (int Width,int Height) Ratio(ScreenSettings screen){int gcd=Gcd(screen.AspectWidth,screen.AspectHeight),w=screen.AspectWidth/gcd,h=screen.AspectHeight/gcd;if(screen.Portrait?w>h:w<h)(w,h)=(h,w);return(w,h);}
    private static int ExactOther(int value,int numerator,int denominator,string dimension){long product=(long)value*numerator;if(product%denominator!=0||product/denominator is <1 or >16384)throw new ArgumentException($"The exact {dimension} cannot satisfy the ratio in whole pixels. Enter compatible dimensions or use automatic sizing.");return(int)(product/denominator);}
    private static int ExplicitWidth(ScreenSettings screen){if(screen.VisibleWidth>0)return screen.VisibleWidth;if(screen.VisibleHeight>0&&!screen.Fill){var ratio=Ratio(screen);return ExactOther(screen.VisibleHeight,ratio.Width,ratio.Height,"visible height");}return 0;}

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

public static class Presets
{
    public static LayoutProfile Triple(int panelHz){if(panelHz<=0)throw new ArgumentOutOfRangeException(nameof(panelHz));return new(new("Left",16,9,true,false,0,Math.Min(30,panelHz)),new("Center",16,9,false,false,0,Math.Min(60,panelHz)),19,false,new("Right",16,9,true,false,0,Math.Min(30,panelHz)),62,1);}
    public static LayoutProfile Balanced(int panelHz)
    {
        if (panelHz <= 0) throw new ArgumentOutOfRangeException(nameof(panelHz));
        var refresh = Math.Min(60, panelHz);
        return new(new("Screen 1", 16, 9, false, false, 0, refresh),
                   new("Screen 2", 16, 9, false, false, 0, refresh), 50, false);
    }

    public static LayoutProfile Gaming(int panelHz, bool portrait = false)
    {
        if (panelHz <= 0) throw new ArgumentOutOfRangeException(nameof(panelHz));
        return new(new("Game", 16, 9, false, false, 0, panelHz),
                   new("Side", 16, 9, portrait, false, 0, 30), 70, true);
    }
}

public sealed class ProfileStore
{
    private readonly string _path;

    public ProfileStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required.", nameof(directory));
        _path = Path.Combine(Path.GetFullPath(directory), "profile.json");
    }

    public LayoutProfile? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var profile = System.Text.Json.JsonSerializer.Deserialize<LayoutProfile>(File.ReadAllText(_path));
            LayoutEngine.ValidateProfile(profile);
            return profile;
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public void Save(LayoutProfile profile)
    {
        LayoutEngine.ValidateProfile(profile);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"profile.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, System.Text.Json.JsonSerializer.Serialize(profile));
            File.Move(tempPath, _path, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
