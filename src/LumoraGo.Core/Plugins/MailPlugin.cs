using LumoraGo.Core.Accounts;
using LumoraGo.Core.Mail;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Plugins;

/// <summary>
/// "/mail": okunmamış mailler (gönderen, konu, zaman). Yazınca gönderen ve konuda süzülür. Enter gelen kutusunu tarayıcıda açar
/// (sağlayıcı bilinmiyorsa konuyu kopyalar). Mailler okunmuş işaretlenmez, gövde indirilmez.
/// </summary>
public sealed class MailPlugin : IPlugin
{
    public const string Keyword = "/mail";
    public static readonly PluginMetadata Metadata = new("lumorago.mail", "Okunmamış mailler", Keyword);

    private const int Shown = 20;

    private readonly Func<MailAccount?> _account;
    private readonly ImapConnect _connect;
    private readonly Action<string> _open;
    private readonly Action _openSettings;
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _keep;
    private readonly object _gate = new();
    private IPluginHost? _host;
    private MailSummary? _summary;
    private string? _error;
    private MailAccount? _loadedFor;
    private DateTime _loadedAt;
    private bool _loading;

    public MailPlugin(Func<MailAccount?> account, ImapConnect connect, Action<string> open, Action openSettings, Func<DateTime>? now = null, TimeSpan? keep = null)
    {
        _account = account;
        _connect = connect;
        _open = open;
        _openSettings = openSettings;
        _now = now ?? (() => DateTime.Now);
        _keep = keep ?? TimeSpan.FromMinutes(2);
    }

    public void Init(PluginContext context)
    {
        _host = context.Host;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct)
    {
        if (_account() is not { } account)
        {
            return One(new Result("Mail hesabı bağlı değil", "Enter ile Ayarlar'ı aç ve mail adresi ile uygulama şifresi gir", null, 1000, _ =>
            {
                _openSettings();
                return true;
            }));
        }

        MailSummary? summary;
        string? error;
        bool stale;
        lock (_gate)
        {
            summary = _loadedFor == account ? _summary : null;
            error = _loadedFor == account ? _error : null;
            stale = _loadedFor != account || _now() - _loadedAt > _keep;
        }

        if (stale)
        {
            StartLoad(account);
        }

        if (summary is null && error is null)
        {
            return One(new Result("Mailler yükleniyor…", null, null, 1000, _ => false));
        }

        var results = new List<Result>();
        if (error is not null)
        {
            results.Add(new Result($"Mailler alınamadı — {error}", "Enter ile Ayarlar'ı aç", null, 1000, _ =>
            {
                _openSettings();
                return true;
            }));
        }

        if (summary is not null)
        {
            var filter = FuzzyMatcher.Fold(query.Search.Trim());
            var webmail = MailProviders.WebmailFor(account);
            var matching = summary.Latest
                .Where(h => filter.Length == 0 || FuzzyMatcher.Score(filter, h.From + " " + h.Subject) > 0)
                .ToList();
            for (var i = 0; i < matching.Count && i < Shown; i++)
            {
                var header = matching[i];
                var ago = header.Date is { } date ? RecentFilesPlugin.Ago(date.LocalDateTime, _now()) : "";
                var action = webmail is null ? "Enter ile konuyu kopyala" : "Enter ile gelen kutusunu aç";
                results.Add(new Result(header.Subject, $"{header.From}{(ago.Length > 0 ? " · " + ago : "")} · {action}", null, 900 - i, _ =>
                {
                    if (webmail is not null)
                    {
                        _open(webmail);
                    }
                    else
                    {
                        _host?.CopyToClipboard(header.Subject);
                    }

                    return true;
                }));
            }

            if (summary.Unread == 0)
            {
                results.Add(new Result("Okunmamış mail yok", account.Address, null, 1, _ => false));
            }
            else if (filter.Length == 0 && summary.Unread > summary.Latest.Count)
            {
                results.Add(new Result($"Toplam {summary.Unread} okunmamış mail", $"En yeni {summary.Latest.Count} tanesi gösteriliyor", null, 1, _ => false));
            }
            else if (matching.Count == 0)
            {
                results.Add(new Result("Eşleşen mail yok", null, null, 1, _ => false));
            }
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private void StartLoad(MailAccount account)
    {
        lock (_gate)
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
        }

        _ = Task.Run(async () =>
        {
            MailSummary? summary = null;
            string? error = null;
            try
            {
                summary = await ImapClient.FetchUnreadAsync(_connect, account, Shown, CancellationToken.None);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _host?.Log(LogLevel.Warning, $"{Keyword} başarısız: {ex.Message}");
            }
            finally
            {
                lock (_gate)
                {
                    _summary = summary;
                    _error = error;
                    _loadedFor = account;
                    _loadedAt = _now();
                    _loading = false;
                }

                _host?.RefreshResults(Keyword);
            }
        });
    }

    private static Task<IReadOnlyList<Result>> One(Result result) => Task.FromResult<IReadOnlyList<Result>>([result]);
}
