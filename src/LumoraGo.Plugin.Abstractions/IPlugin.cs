namespace LumoraGo.Plugin;

/// <summary>
/// Her LumoraGo eklentisinin uyguladığı arayüz. Çekirdek eklentiyi yalnızca bu arayüzden tanır.
/// </summary>
public interface IPlugin
{
    /// <summary>Eklenti yüklendiğinde bir kez çağrılır. Uzun süren hazırlıklar (indeksleme, ağ) burada veya arka planda yapılır.</summary>
    void Init(PluginContext context);

    /// <summary>
    /// Her tuş vuruşunda çağrılır. Hızlı dönmeli; yeni bir tuşa basıldığında <paramref name="ct"/> iptal edilir.
    /// </summary>
    Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken ct);
}
