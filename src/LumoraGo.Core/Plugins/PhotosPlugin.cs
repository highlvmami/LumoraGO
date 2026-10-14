using System.Globalization;
using LumoraGo.Core.Contacts;
using LumoraGo.Core.Photos;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/foto": Google Fotoğraflar'dan seçilenler ve bilgisayardaki fotoğraf klasörü, en yenisi başta; yazınca ada,
/// klasöre ya da tarihe ("2024", "mart") göre süzülür. Seçili fotoğraf yandaki önizlemede görünür.
/// </summary>
public sealed class PhotosPlugin : IPlugin
{
    public const string Keyword = "/foto";

    /// <summary>Simge yolu bu önekle başlarsa uygulama dosyanın küçük resmini çizer.</summary>
    public const string ThumbnailPrefix = "thumb:";

    private const int MaxResults = 300;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly PhotoLibrary _library;
    private readonly GooglePhotoImport _import;
    private readonly Func<bool> _googleConnected;
    private readonly Action<string> _open;
    private readonly Action<string> _reveal;
    private readonly Action? _openSettings;
    private IPluginHost? _host;
    private string? _status;

    public PhotosPlugin(
        PhotoLibrary library, GooglePhotoImport import, Func<bool> googleConnected, Action<string> open, Action<string> reveal,
        Action? openSettings = null, string keyword = Keyword)
    {
        _library = library;
        _import = import;
        _googleConnected = googleConnected;
        _open = open;
        _reveal = reveal;
        _openSettings = openSettings;
        Metadata = new PluginMetadata(keyword == Keyword ? "lumorago.photos" : "lumorago.photos." + keyword.Trim('/'), "Fotoğraflar", keyword);
    }

    public PluginMetadata Metadata { get; }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var text = query.Search.Trim();
        var folded = FuzzyMatcher.Fold(text);
        var photos = _library.Google.Concat(_library.Local(Refresh))
            .Select(p => (Photo: p, Score: folded.Length == 0 ? 1 : Match(folded, p)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => folded.Length == 0 ? 0 : m.Score)
            .ThenByDescending(m => m.Photo.Date)
            .Take(MaxResults)
            .Select((m, i) => Row(m.Photo, MaxResults - i))
            .ToList();

        var results = new List<Result>();
        if (folded.Length == 0)
        {
            results.Add(PickRow());
        }

        results.AddRange(photos);
        if (photos.Count == 0 && folded.Length > 0)
        {
            results.Add(new Result("Fotoğraf bulunamadı", $"\"{text}\" adında ya da tarihinde fotoğraf yok", null, 0, _ => false));
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static int Match(string folded, Photo photo)
    {
        var haystack = $"{photo.Name} {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(photo.Path))} {photo.Date.ToString("d MMMM yyyy", Turkish)}";
        return FuzzyMatcher.Score(folded, haystack);
    }

    private Result PickRow()
    {
        var count = _library.Google.Count;
        if (!_googleConnected())
        {
            return new Result("Google Fotoğraflar'ı bağla", "Ayarlar → Kişiler'den \"Google ile bağlan\", sonra buradan fotoğraf seç", null, MaxResults + 1, _ =>
            {
                _openSettings?.Invoke();
                return true;
            });
        }

        var subtitle = _import.IsRunning
            ? "Tarayıcıda Google Fotoğraflar açık; seçip \"Bitti\"ye bas"
            : _status ?? (count == 0
                ? "Tarayıcıda Google'ın penceresi açılır; seçtiklerin burada önizlemeyle görünür"
                : $"{count} fotoğraf seçili · yenilerini eklemek için Enter");
        var menu = count == 0
            ? null
            : new List<ContextAction> { new("Google'dan seçilenlerin hepsini listeden kaldır", _ => { _library.ClearGoogle(); _status = null; Refresh(); return false; }) };
        return new Result("Google Fotoğraflar'dan fotoğraf seç", subtitle, null, MaxResults + 1, ctx =>
        {
            _ = PickAsync();
            return true;
        }, menu);
    }

    private async Task PickAsync()
    {
        _status = null;
        try
        {
            var added = await _import.PickAsync(CancellationToken.None);
            if (added is { } n)
            {
                _status = n == 0 ? "Hiç fotoğraf seçilmedi" : $"{n} fotoğraf eklendi";
                _host?.ShowMessage("Google Fotoğraflar", _status);
            }
        }
        catch (GoogleException ex)
        {
            _status = ex.Message;
            _host?.ShowMessage("Google Fotoğraflar", ex.Message);
        }

        Refresh();
    }

    private void Refresh() => _host?.RefreshResults(Metadata.ActionKeyword!);

    private Result Row(Photo photo, int score)
    {
        var where = photo.FromGoogle ? "Google Fotoğraflar" : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(photo.Path)) ?? "";
        var menu = new List<ContextAction>
        {
            new("Klasörde göster", _ => { _reveal(photo.Path); return true; }),
            new("Yolu kopyala", _ => { _host?.CopyToClipboard(photo.Path); return true; }),
        };
        if (photo.FromGoogle)
        {
            menu.Add(new ContextAction("Listeden kaldır", _ => { _library.RemoveGoogle(photo); Refresh(); return false; }));
        }

        return new Result(photo.Name, $"{where} · {photo.Date.ToString("d MMMM yyyy", Turkish)}", ThumbnailPrefix + photo.Path, score, _ =>
        {
            _open(photo.Path);
            return true;
        }, menu)
        {
            PreviewPath = photo.Path,
            DragData = DragPayload.File(photo.Path),
        };
    }
}
