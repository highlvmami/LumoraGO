using LumoraGo.Core.Info;
using LumoraGo.Core.Plugins;

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var football = new FootballService(http);
foreach (var q in new[] { "fenerbahçe", "fb", "galatasaray", "bjk", "arsenal", "" })
{
    Console.WriteLine($"== /mac {q}");
    foreach (var r in await InfoPlugins.MatchesAsync(football, q, _ => { }, CancellationToken.None))
    {
        Console.WriteLine(r.Card is { } c ? $"[KART] {r.Title}: {c.Headline} | {c.Caption}" : $"{r.Title} | {r.Subtitle}");
    }
}

foreach (var q in new[] { "", "premier" })
{
    Console.WriteLine($"== /lig {q}");
    foreach (var r in (await InfoPlugins.TableAsync(football, q, _ => { }, CancellationToken.None)).Take(20))
    {
        Console.WriteLine($"{r.Title} | {r.Subtitle}");
    }
}
