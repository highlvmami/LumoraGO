namespace LumoraGo.Core.Desktop;

/// <param name="Name">Sürücü harfi, örn. "C:".</param>
public sealed record DiskUsage(string Name, string Label, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercent => TotalBytes > 0 ? UsedBytes * 100.0 / TotalBytes : 0;
}

/// <param name="Percent">0-100.</param>
/// <param name="Charging">Prize takılı mı.</param>
/// <param name="Remaining">Windows'un tahmini; bilinmiyorsa null.</param>
public sealed record BatteryStatus(int Percent, bool Charging, TimeSpan? Remaining);

/// <summary>"/sistem" kartlarının verisi; uygulama tarafında Windows API'leriyle doldurulur.</summary>
/// <param name="CpuPercent">Son yarım saniyedeki toplam işlemci kullanımı.</param>
/// <param name="TopProcesses">En çok bellek kullanan uygulamalar (aynı addaki işlemler toplanmış).</param>
public sealed record SystemSnapshot(
    double CpuPercent,
    long MemoryTotalBytes,
    long MemoryUsedBytes,
    IReadOnlyList<DiskUsage> Disks,
    BatteryStatus? Battery,
    TimeSpan Uptime,
    IReadOnlyList<RunningProcess> TopProcesses);
