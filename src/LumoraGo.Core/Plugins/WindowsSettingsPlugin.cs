using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>"bluetooth", "ekran", "varsayılan uygulamalar" gibi yazınca ilgili Windows Ayarları sayfasını önerir.</summary>
public sealed class WindowsSettingsPlugin(Action<string> open) : IPlugin
{
    public static readonly PluginMetadata Metadata = new("lumorago.winsettings", "Windows ayarları");

    /// <summary>Ayar sayfaları uygulamaların altında dursun.</summary>
    public const int MaxScore = 60;

    internal static readonly (string Title, string Uri, string Words)[] Pages =
    [
        ("Wi-Fi", "ms-settings:network-wifi", "wifi kablosuz internet ag"),
        ("Ağ ve İnternet", "ms-settings:network", "ag internet ethernet network"),
        ("VPN", "ms-settings:network-vpn", "vpn"),
        ("Proxy", "ms-settings:network-proxy", "proxy vekil sunucu"),
        ("Bluetooth ve cihazlar", "ms-settings:bluetooth", "bluetooth kulaklik cihaz"),
        ("Yazıcılar ve tarayıcılar", "ms-settings:printers", "yazici tarayici printer"),
        ("Fare", "ms-settings:mousetouchpad", "fare mouse imlec"),
        ("Dokunmatik yüzey", "ms-settings:devices-touchpad", "touchpad dokunmatik"),
        ("Ekran", "ms-settings:display", "ekran cozunurluk olcek monitor parlaklik display"),
        ("Gece ışığı", "ms-settings:nightlight", "gece isigi mavi isik"),
        ("Ses", "ms-settings:sound", "ses hoparlor mikrofon sound"),
        ("Bildirimler", "ms-settings:notifications", "bildirim notification"),
        ("Odaklanma", "ms-settings:quiethours", "odak rahatsiz etme focus"),
        ("Güç ve pil", "ms-settings:powersleep", "guc pil uyku batarya power"),
        ("Depolama", "ms-settings:storagesense", "depolama disk alan storage"),
        ("Pano", "ms-settings:clipboard", "pano clipboard"),
        ("Uzak Masaüstü", "ms-settings:remotedesktop", "uzak masaustu remote"),
        ("Hakkında", "ms-settings:about", "hakkinda sistem bilgisi bilgisayar adi about"),
        ("Kişiselleştirme / arka plan", "ms-settings:personalization-background", "arka plan duvar kagidi wallpaper kisisellestirme"),
        ("Renkler ve karanlık mod", "ms-settings:colors", "renk karanlik mod koyu tema dark"),
        ("Temalar", "ms-settings:themes", "tema theme"),
        ("Kilit ekranı", "ms-settings:lockscreen", "kilit ekrani lock"),
        ("Görev çubuğu", "ms-settings:taskbar", "gorev cubugu taskbar"),
        ("Yazı tipleri", "ms-settings:fonts", "yazi tipi font"),
        ("Yüklü uygulamalar", "ms-settings:appsfeatures", "uygulama kaldir program yuklu apps"),
        ("Varsayılan uygulamalar", "ms-settings:defaultapps", "varsayilan uygulama tarayici default"),
        ("Başlangıç uygulamaları", "ms-settings:startupapps", "baslangic acilis startup"),
        ("Hesap bilgileri", "ms-settings:yourinfo", "hesap profil account"),
        ("Oturum açma seçenekleri", "ms-settings:signinoptions", "oturum acma sifre pin parmak izi hello"),
        ("Aile ve diğer kullanıcılar", "ms-settings:otherusers", "kullanici aile hesap ekle"),
        ("Tarih ve saat", "ms-settings:dateandtime", "tarih saat saat dilimi time"),
        ("Dil ve bölge", "ms-settings:regionlanguage", "dil bolge klavye language"),
        ("Yazma ve klavye", "ms-settings:typing", "klavye yazma otomatik duzelt typing"),
        ("Oyun modu", "ms-settings:gaming-gamemode", "oyun game"),
        ("Erişilebilirlik", "ms-settings:easeofaccess", "erisilebilirlik buyutec metin boyutu"),
        ("Gizlilik ve güvenlik", "ms-settings:privacy", "gizlilik guvenlik izin privacy"),
        ("Konum", "ms-settings:privacy-location", "konum location gps"),
        ("Kamera izinleri", "ms-settings:privacy-webcam", "kamera webcam izin"),
        ("Mikrofon izinleri", "ms-settings:privacy-microphone", "mikrofon izin"),
        ("Windows Güvenliği", "windowsdefender:", "guvenlik virus defender antivirus"),
        ("Windows Update", "ms-settings:windowsupdate", "guncelleme update windows update"),
        ("Kurtarma", "ms-settings:recovery", "kurtarma sifirla reset recovery"),
        ("Etkinleştirme", "ms-settings:activation", "etkinlestirme lisans aktivasyon"),
        ("Geliştiriciler için", "ms-settings:developers", "gelistirici developer"),
    ];

    public void Init(PluginContext context)
    {
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        var folded = FuzzyMatcher.Fold(query.Search.Trim());
        if (folded.Length < 3)
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var results = Pages
            .Select(p => (Page: p, Score: Match(folded, p.Title, p.Words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select((x, i) => new Result(x.Page.Title, "Windows Ayarları", null, MaxScore - i, _ =>
            {
                open(x.Page.Uri);
                return true;
            }))
            .ToList();

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static int Match(string folded, string title, string words)
    {
        var foldedTitle = FuzzyMatcher.Fold(title);
        if (foldedTitle.StartsWith(folded, StringComparison.Ordinal))
        {
            return 3;
        }

        var all = foldedTitle + " " + words;
        if (all.Split(' ').Any(w => w.StartsWith(folded, StringComparison.Ordinal)))
        {
            return 2;
        }

        return folded.Length >= 4 && all.Contains(folded, StringComparison.Ordinal) ? 1 : 0;
    }
}
