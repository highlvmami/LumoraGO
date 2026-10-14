using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

public enum WindowPlacement
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftThird,
    CenterThird,
    RightThird,
    LeftTwoThirds,
    RightTwoThirds,
    Center,
    Maximize,
    Minimize,
    NextScreen,
}

/// <param name="Left">Ekranın çalışma alanındaki (görev çubuğu hariç) konum, piksel.</param>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height);

/// <summary>
/// Pencere yerleştirme: "sol yarı", "sağ üst", "ortala", "tam ekran", "diğer ekran"… Launcher açılmadan önce öndeki pencereye
/// uygulanır. "/pencere" hepsini listeler; genel aramada da adı yazınca çıkar.
/// </summary>
public sealed class WindowLayoutPlugin(Action<WindowPlacement> apply) : IPlugin
{
    public const string Keyword = "/pencere";
    public static readonly PluginMetadata GlobalMetadata = new("lumorago.windowlayout", "Pencere yerleştirme");

    public static readonly IReadOnlyList<(WindowPlacement Placement, string Title, string[] Words)> All =
    [
        (WindowPlacement.LeftHalf, "Sol yarı", ["sol yari", "sola yasla", "sol"]),
        (WindowPlacement.RightHalf, "Sağ yarı", ["sag yari", "saga yasla", "sag"]),
        (WindowPlacement.TopHalf, "Üst yarı", ["ust yari"]),
        (WindowPlacement.BottomHalf, "Alt yarı", ["alt yari"]),
        (WindowPlacement.TopLeft, "Sol üst çeyrek", ["sol ust"]),
        (WindowPlacement.TopRight, "Sağ üst çeyrek", ["sag ust"]),
        (WindowPlacement.BottomLeft, "Sol alt çeyrek", ["sol alt"]),
        (WindowPlacement.BottomRight, "Sağ alt çeyrek", ["sag alt"]),
        (WindowPlacement.LeftThird, "Sol üçte bir", ["sol ucte bir", "sol 1/3"]),
        (WindowPlacement.CenterThird, "Orta üçte bir", ["orta ucte bir", "orta 1/3"]),
        (WindowPlacement.RightThird, "Sağ üçte bir", ["sag ucte bir", "sag 1/3"]),
        (WindowPlacement.LeftTwoThirds, "Sol üçte iki", ["sol ucte iki", "sol 2/3"]),
        (WindowPlacement.RightTwoThirds, "Sağ üçte iki", ["sag ucte iki", "sag 2/3"]),
        (WindowPlacement.Center, "Ortala", ["ortala", "ortaya al"]),
        (WindowPlacement.Maximize, "Tam ekran", ["tam ekran", "buyut", "ekrani kapla"]),
        (WindowPlacement.Minimize, "Küçült", ["kucult", "simge durumuna"]),
        (WindowPlacement.NextScreen, "Diğer ekrana taşı", ["diger ekran", "sonraki ekran", "ekran degistir"]),
    ];

    public static readonly PluginMetadata KeywordMetadata = new("lumorago.windowlayout.kw", "Pencere yerleştirme", Keyword);

    private readonly bool _keyword;

    public WindowLayoutPlugin(Action<WindowPlacement> apply, bool keyword) : this(apply)
    {
        _keyword = keyword;
    }

    public PluginMetadata Metadata => _keyword ? KeywordMetadata : GlobalMetadata;

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(query.Search.Trim());
        IEnumerable<(WindowPlacement Placement, string Title, string[] Words)> matches;
        if (_keyword)
        {
            matches = folded.Length == 0 ? All : All.Where(a => Matches(a, folded, 1));
        }
        else
        {
            // Genel aramada "sol" gibi kısa kelimeler başka sonuçları ezmesin: en az 5 harf ya da tam kelime.
            matches = folded.Length < 3 ? [] : All.Where(a => a.Words.Any(w => w == folded) || (folded.Length >= 5 && Matches(a, folded, 5)));
        }

        var results = matches.Select((a, i) => new Result(a.Title, "Öndeki pencere · Enter", null, (_keyword ? 1000 : 900) - i, _ =>
        {
            apply(a.Placement);
            return true;
        })).ToList();
        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static bool Matches((WindowPlacement Placement, string Title, string[] Words) entry, string folded, int min) =>
        folded.Length >= min && (FuzzyMatcher.Fold(entry.Title).StartsWith(folded, StringComparison.Ordinal) || entry.Words.Any(w => w.StartsWith(folded, StringComparison.Ordinal)));

    /// <summary>Yerleşimin çalışma alanındaki dikdörtgeni. Ortala/küçült/diğer ekran için <paramref name="current"/> kullanılır.</summary>
    public static ScreenRect Target(WindowPlacement placement, ScreenRect work, ScreenRect current)
    {
        int W(double f) => (int)Math.Round(work.Width * f);
        int H(double f) => (int)Math.Round(work.Height * f);
        var (l, t, w, h) = (work.Left, work.Top, work.Width, work.Height);
        return placement switch
        {
            WindowPlacement.LeftHalf => new(l, t, W(0.5), h),
            WindowPlacement.RightHalf => new(l + W(0.5), t, w - W(0.5), h),
            WindowPlacement.TopHalf => new(l, t, w, H(0.5)),
            WindowPlacement.BottomHalf => new(l, t + H(0.5), w, h - H(0.5)),
            WindowPlacement.TopLeft => new(l, t, W(0.5), H(0.5)),
            WindowPlacement.TopRight => new(l + W(0.5), t, w - W(0.5), H(0.5)),
            WindowPlacement.BottomLeft => new(l, t + H(0.5), W(0.5), h - H(0.5)),
            WindowPlacement.BottomRight => new(l + W(0.5), t + H(0.5), w - W(0.5), h - H(0.5)),
            WindowPlacement.LeftThird => new(l, t, W(1 / 3.0), h),
            WindowPlacement.CenterThird => new(l + W(1 / 3.0), t, W(2 / 3.0) - W(1 / 3.0), h),
            WindowPlacement.RightThird => new(l + W(2 / 3.0), t, w - W(2 / 3.0), h),
            WindowPlacement.LeftTwoThirds => new(l, t, W(2 / 3.0), h),
            WindowPlacement.RightTwoThirds => new(l + W(1 / 3.0), t, w - W(1 / 3.0), h),
            WindowPlacement.Center => Centered(Math.Min(current.Width, w), Math.Min(current.Height, h), work),
            _ => work,
        };
    }

    /// <summary>Diğer ekrana taşırken pencere aynı oranda ve aynı göreli konumda kalır.</summary>
    public static ScreenRect MoveTo(ScreenRect current, ScreenRect fromWork, ScreenRect toWork)
    {
        var sx = (double)toWork.Width / fromWork.Width;
        var sy = (double)toWork.Height / fromWork.Height;
        var width = Math.Min((int)Math.Round(current.Width * sx), toWork.Width);
        var height = Math.Min((int)Math.Round(current.Height * sy), toWork.Height);
        var left = toWork.Left + (int)Math.Round((current.Left - fromWork.Left) * sx);
        var top = toWork.Top + (int)Math.Round((current.Top - fromWork.Top) * sy);
        left = Math.Clamp(left, toWork.Left, toWork.Left + toWork.Width - width);
        top = Math.Clamp(top, toWork.Top, toWork.Top + toWork.Height - height);
        return new(left, top, width, height);
    }

    private static ScreenRect Centered(int width, int height, ScreenRect work) =>
        new(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
}
