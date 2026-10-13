using LumoraGo.Plugin;

namespace LumoraGo.Core.Files;

/// <summary>Dosya sonuçları için ortak satır: Enter açar, Ctrl+Enter klasöründe gösterir, sağ ok menüsünde yolu kopyalama var.</summary>
public sealed class FileResults(Action<string> open, Action<string> reveal)
{
    private IPluginHost? _host;

    public void Init(IPluginHost host) => _host = host;

    public Result Create(string path, bool isDirectory, string title, string subtitle, int score) =>
        new(title, subtitle, isDirectory ? null : path, score, ctx =>
        {
            if (ctx.Modifiers.HasFlag(ActionModifiers.Ctrl))
            {
                reveal(path);
            }
            else
            {
                open(path);
            }

            return true;
        },
        [
            new ContextAction("Aç", _ =>
            {
                open(path);
                return true;
            }),
            new ContextAction("Klasöründe göster", _ =>
            {
                reveal(path);
                return true;
            }),
            new ContextAction("Yolu kopyala", _ =>
            {
                _host?.CopyToClipboard(path);
                return true;
            }),
        ])
        {
            PreviewPath = path,
        };
}
