using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using LumoraGo.Core.Accounts;

namespace LumoraGo.Core.Ai;

public sealed class AiException(string message) : Exception(message);

/// <summary>Soruyu yanıtlayan model; testlerde sahtesi verilir.</summary>
public interface IAssistant
{
    bool IsConfigured { get; }

    /// <exception cref="AiException">Anahtar yoksa ya da istek başarısızsa; mesajı kullanıcıya gösterilir.</exception>
    Task<string> AskAsync(string instructions, string prompt, CancellationToken ct);
}

/// <summary>Claude API, kullanıcının Ayarlar'a yapıştırdığı kendi API anahtarıyla.</summary>
/// <param name="http">Testlerde sahte istemci; null ise SDK'nın kendi istemcisi.</param>
public sealed class ClaudeAssistant(SecretStore secrets, HttpClient? http = null) : IAssistant
{
    private const string ModelId = "claude-opus-5-5";

    public bool IsConfigured => secrets.Current.AnthropicApiKey is { Length: > 0 };

    public async Task<string> AskAsync(string instructions, string prompt, CancellationToken ct)
    {
        if (secrets.Current.AnthropicApiKey is not { Length: > 0 } key)
        {
            throw new AiException(NotConfigured);
        }

        var client = http is null ? new AnthropicClient { ApiKey = key } : new AnthropicClient { ApiKey = key, HttpClient = http, MaxRetries = 0 };
        try
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = 16000,
                System = instructions,
                // Launcher'da hızlı yanıt önemli; zor sorular için bile düşük çaba çoğunlukla yeterli.
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
                // Güvenlik sınıflandırıcısı reddederse istek aynı çağrıda başka modelle yanıtlanır.
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                Messages = [new() { Role = Role.User, Content = prompt }],
            }, ct);

            if (response.StopReason == "refusal")
            {
                throw new AiException("Claude bu isteği yanıtlamadı.");
            }

            var text = string.Join("\n\n", response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text)).Trim();
            return text.Length > 0 ? text : throw new AiException("Claude boş yanıt verdi.");
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new AiException("API anahtarı geçersiz. Ayarlar → Yapay zekâ'daki anahtarı kontrol et.");
        }
        catch (AnthropicRateLimitException)
        {
            throw new AiException("Çok fazla istek; biraz sonra tekrar dene.");
        }
        catch (AnthropicApiException ex)
        {
            throw new AiException($"Claude hata verdi: {ex.Message}");
        }
        catch (AnthropicIOException ex)
        {
            throw new AiException($"Claude'a bağlanılamadı ({ex.Message})");
        }
    }

    public const string NotConfigured = "Claude API anahtarı yok. Ayarlar → Yapay zekâ'ya console.anthropic.com'dan aldığın anahtarı yapıştır.";
}
