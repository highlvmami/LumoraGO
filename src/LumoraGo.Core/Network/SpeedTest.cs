using System.Diagnostics;
using System.Globalization;

namespace LumoraGo.Core.Network;

/// <summary>
/// İnternet hız testi: Cloudflare'ın herkese açık ölçüm uçlarından indirme/yükleme süresi ve gecikmeyi ölçer.
/// Yaklaşık 25 MB indirir ve 8 MB yükler; sınırlar özelliklerden değiştirilebilir.
/// </summary>
public sealed class SpeedTest(HttpClient http)
{
    public const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=";
    public const string UploadUrl = "https://speed.cloudflare.com/__up";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public long DownloadBytes { get; init; } = 25_000_000;

    public TimeSpan DownloadLimit { get; init; } = TimeSpan.FromSeconds(10);

    public long UploadBytes { get; init; } = 8_000_000;

    public async Task RunAsync(Action<IReadOnlyList<NetLine>> report, CancellationToken ct)
    {
        string? latency = null, download = null, upload = null;
        var status = "Gecikme ölçülüyor…";

        void Push()
        {
            var lines = new List<NetLine>();
            if (download is not null)
            {
                lines.Add(new NetLine(download, "İndirme hızı", download));
            }

            if (upload is not null)
            {
                lines.Add(new NetLine(upload, "Yükleme hızı", upload));
            }

            if (latency is not null)
            {
                lines.Add(new NetLine(latency, "Gecikme", latency));
            }

            if (status.Length > 0)
            {
                lines.Add(new NetLine(status));
            }

            report(lines);
        }

        Push();

        // Gecikme: boş indirmelerin ortanca süresi.
        var times = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync(DownloadUrl + "0", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsByteArrayAsync(ct);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        latency = $"{times[times.Count / 2]:0} ms";
        status = "İndirme ölçülüyor…";
        Push();

        // İndirme: süre sınırına ya da bayt hedefine kadar.
        using (var response = await http.GetAsync(DownloadUrl + DownloadBytes, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[64 * 1024];
            long total = 0;
            var sw = Stopwatch.StartNew();
            var lastPush = TimeSpan.Zero;
            int read;
            while (sw.Elapsed < DownloadLimit && (read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (sw.Elapsed - lastPush > TimeSpan.FromMilliseconds(400))
                {
                    lastPush = sw.Elapsed;
                    download = Mbps(total, sw.Elapsed);
                    Push();
                }
            }

            download = Mbps(total, sw.Elapsed);
        }

        status = "Yükleme ölçülüyor…";
        Push();

        var payload = new ByteArrayContent(new byte[UploadBytes]);
        var uploadWatch = Stopwatch.StartNew();
        using (var response = await http.PostAsync(UploadUrl, payload, ct))
        {
            response.EnsureSuccessStatusCode();
        }

        upload = Mbps(UploadBytes, uploadWatch.Elapsed);
        status = "";
        Push();
    }

    internal static string Mbps(long bytes, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        return $"{(bytes * 8 / 1_000_000.0 / seconds).ToString("0.#", Turkish)} Mbps";
    }
}
