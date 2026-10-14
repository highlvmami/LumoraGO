using System.Net;
using System.Text.Json;
using LumoraGo.Core.Accounts;
using LumoraGo.Core.Ai;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public sealed class AiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ai-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeAssistant(bool configured = true) : IAssistant
    {
        public List<(string Instructions, string Prompt)> Calls { get; } = [];

        public TaskCompletionSource<string> Next { get; set; } = new();

        public bool IsConfigured => configured;

        public Task<string> AskAsync(string instructions, string prompt, CancellationToken ct)
        {
            Calls.Add((instructions, prompt));
            return Next.Task;
        }
    }

    private sealed class Host : IPluginHost
    {
        public List<string> Refreshed { get; } = [];
        public string? Copied { get; private set; }
        public void ChangeQuery(string text) { }
        public void HideLauncher() { }
        public void CopyToClipboard(string text) => Copied = text;
        public void RefreshResults(string keyword) { lock (Refreshed) { Refreshed.Add(keyword); } }
        public void ShowMessage(string title, string message) { }
        public void Log(LogLevel level, string message) { }
    }

    private static readonly ActionContext Enter = new(ActionModifiers.None);

    [Fact]
    public async Task Question_IsSentOnlyOnEnterAndAnswerReplacesRow()
    {
        var assistant = new FakeAssistant();
        var host = new Host();
        var plugin = new AiPlugin(assistant, () => null, _ => { }, () => { }, keyword: null);
        plugin.Init(new PluginContext(plugin.Metadata, "", host));
        var query = new Query("?başkent neresi", null, "?başkent neresi");

        var rows = await plugin.QueryAsync(query, CancellationToken.None);
        Assert.Equal("Claude'a sor: başkent neresi", Assert.Single(rows).Title);
        Assert.Empty(assistant.Calls);

        Assert.False(rows[0].Action(Enter));
        Assert.Equal("başkent neresi", Assert.Single(assistant.Calls).Prompt);
        Assert.Equal("Claude düşünüyor…", (await plugin.QueryAsync(query, CancellationToken.None))[0].Title);

        assistant.Next.SetResult("Ankara.\nTürkiye'nin başkentidir.");
        for (var i = 0; i < 50 && host.Refreshed.Count < 2; i++)
        {
            await Task.Delay(10);
        }

        Assert.Contains("?başkent neresi", host.Refreshed);
        var answer = Assert.Single(await plugin.QueryAsync(query, CancellationToken.None));
        Assert.Equal("Ankara.", answer.Title);
        Assert.Equal(["Ankara.", "Türkiye'nin başkentidir."], answer.Card!.Lines!);
        answer.Action(Enter);
        Assert.Equal("Ankara.\nTürkiye'nin başkentidir.", host.Copied);
    }

    [Fact]
    public async Task Global_IgnoresTextWithoutQuestionMark()
    {
        var plugin = new AiPlugin(new FakeAssistant(), () => "x", _ => { }, () => { }, keyword: null);
        Assert.Empty(await plugin.QueryAsync(new Query("başkent", null, "başkent"), CancellationToken.None));
    }

    [Fact]
    public async Task Keyword_OffersClipboardActionsAndFilters()
    {
        var assistant = new FakeAssistant();
        var pasted = new List<string>();
        var plugin = new AiPlugin(assistant, () => "bu metin cok hatali", pasted.Add, () => { });
        var all = await plugin.QueryAsync(new Query("/ai", "/ai", ""), CancellationToken.None);
        Assert.Equal(AiPlugin.Actions.Count, all.Count);

        var fix = await plugin.QueryAsync(new Query("/ai düzelt", "/ai", "düzelt"), CancellationToken.None);
        Assert.Equal("Panodakinin yazımını düzelt", fix[0].Title);
        Assert.Equal("Claude'a sor: düzelt", fix[^1].Title);
        fix[0].Action(Enter);
        Assert.Equal("bu metin cok hatali", assistant.Calls[0].Prompt);
        Assert.Contains("yazım", assistant.Calls[0].Instructions);

        assistant.Next.SetResult("Bu metin çok hatalı.");
        await Task.Delay(50);
        var answer = (await plugin.QueryAsync(new Query("/ai düzelt", "/ai", "düzelt"), CancellationToken.None))[0];
        answer.Action(new ActionContext(ActionModifiers.Ctrl));
        Assert.Equal(["Bu metin çok hatalı."], pasted);
    }

    [Fact]
    public async Task Failure_ShowsErrorAndAllowsRetry()
    {
        var assistant = new FakeAssistant();
        var plugin = new AiPlugin(assistant, () => null, _ => { }, () => { });
        var query = new Query("/ai merhaba", "/ai", "merhaba");
        (await plugin.QueryAsync(query, CancellationToken.None))[0].Action(Enter);
        assistant.Next.SetException(new AiException("API anahtarı geçersiz."));
        await Task.Delay(50);
        var row = Assert.Single(await plugin.QueryAsync(query, CancellationToken.None));
        Assert.Contains("API anahtarı geçersiz.", row.Subtitle);
        assistant.Next = new TaskCompletionSource<string>();
        row.Action(Enter);
        Assert.Equal(2, assistant.Calls.Count);
    }

    [Fact]
    public async Task NotConfigured_PointsToSettings()
    {
        var opened = false;
        var plugin = new AiPlugin(new FakeAssistant(configured: false), () => null, _ => { }, () => opened = true);
        var row = Assert.Single(await plugin.QueryAsync(new Query("/ai selam", "/ai", "selam"), CancellationToken.None));
        row.Action(Enter);
        Assert.True(opened);
    }

    private SecretStore Secrets(string? key)
    {
        var secrets = new SecretStore(Path.Combine(_dir, Guid.NewGuid() + ".dat"), b => b, b => b);
        secrets.Save(secrets.Current with { AnthropicApiKey = key });
        return secrets;
    }

    [Fact]
    public async Task Claude_SendsModelEffortAndFallback()
    {
        string? body = null;
        string? apiKey = null;
        var handler = new FakeHandler(r =>
        {
            body = r.Content!.ReadAsStringAsync().Result;
            apiKey = r.Headers.TryGetValues("x-api-key", out var v) ? v.Single() : null;
            return FakeHandler.Json("""
                {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"Ankara."}],
                 "stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":3}}
                """);
        });
        var claude = new ClaudeAssistant(Secrets("sk-test"), new HttpClient(handler));
        Assert.Equal("Ankara.", await claude.AskAsync("kısa yanıt", "başkent?", CancellationToken.None));

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal("low", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("default", root.GetProperty("fallbacks").GetString());
        Assert.Equal("kısa yanıt", root.GetProperty("system").GetString());
        Assert.False(root.TryGetProperty("thinking", out _));
        Assert.Equal("sk-test", apiKey);
    }

    [Fact]
    public async Task Claude_ExplainsBadKeyAndMissingKey()
    {
        var bad = new ClaudeAssistant(Secrets("sk-bad"), new HttpClient(new FakeHandler(_ =>
            FakeHandler.Json("""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized))));
        var ex = await Assert.ThrowsAsync<AiException>(() => bad.AskAsync("", "x", CancellationToken.None));
        Assert.Contains("geçersiz", ex.Message);

        var none = new ClaudeAssistant(Secrets(null));
        Assert.False(none.IsConfigured);
        await Assert.ThrowsAsync<AiException>(() => none.AskAsync("", "x", CancellationToken.None));
    }
}
