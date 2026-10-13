using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumoraGo.App.Native;
using LumoraGo.App.Services;
using LumoraGo.Core;
using LumoraGo.Core.Preview;
using LumoraGo.Plugin;

namespace LumoraGo.App.Controls;

/// <summary>
/// Hızlı önizleme paneli (Shift+Boşluk). Dosya okuma, kısayol çözme ve küçük resimler kendi STA iş parçacığında yapılır;
/// panel yalnızca hazır sonucu çizer. Seçim hızla değişirse eski istekler atılır.
/// </summary>
internal sealed class PreviewPane : Border
{
    private const double BodyHeight = 330;

    private readonly StaWorker _worker = new("LumoraGo.Preview");
    private readonly ILog _log;
    private int _version;
    private PreviewHandlerHost? _handlerHost;

    public PreviewPane(ILog log)
    {
        _log = log;
        Padding = new Thickness(16, 14, 16, 14);
        Height = 470;
    }

    /// <summary>Önizleyici odak çalarsa pencere geri almak için dinler.</summary>
    public event EventHandler? DocumentShown;

    public void Show(Result? result)
    {
        var version = ++_version;
        if (result is null || (result.PreviewPath is null && result.PreviewText is null))
        {
            Render(Message(result is null ? "Önizlenecek bir şey seçili değil" : "Bu sonucun önizlemesi yok"));
            return;
        }

        if (result.PreviewText is { } text)
        {
            Render(Build(PreviewBuilder.ForText(result.Title, text), null));
            return;
        }

        var path = result.PreviewPath!;
        _worker.Post(() =>
        {
            if (version != _version)
            {
                return;
            }

            PreviewModel model;
            ImageSource? picture = null;
            Guid? handler = null;
            try
            {
                model = PreviewBuilder.ForPath(ShellPreview.ResolveShortcut(path));
                switch (model.Kind)
                {
                    case PreviewKind.Image:
                        picture = LoadImage(model.Path!) ?? ShellPreview.Thumbnail(model.Path!, 512);
                        break;
                    case PreviewKind.Document:
                        handler = ShellPreview.FindHandler(System.IO.Path.GetExtension(model.Path!));
                        if (handler is null)
                        {
                            picture = ShellPreview.Thumbnail(model.Path!, 512);
                        }

                        break;
                    case PreviewKind.Media or PreviewKind.Info:
                        picture = model.Path is null ? null : ShellPreview.Thumbnail(model.Path, 256);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Önizleme hazırlanamadı: {path}: {ex.Message}");
                model = new PreviewModel(PreviewKind.Info, System.IO.Path.GetFileName(path), path, [("Durum", "Önizleme hazırlanamadı")]);
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (version == _version)
                {
                    Render(Build(model, picture, handler));
                }
            });
        });
    }

    public void Clear()
    {
        _version++;
        Render(null);
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 800;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or System.IO.FileFormatException)
        {
            return null;
        }
    }

    private void Render(UIElement? content)
    {
        // Eski önizleyici penceresini kapat (DestroyWindowCore, önizleyiciyi de bırakır).
        _handlerHost?.Dispose();
        _handlerHost = null;
        Child = content;
    }

    private UIElement Build(PreviewModel model, ImageSource? picture, Guid? handler = null)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = model.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 44,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 10),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        grid.Children.Add(title);

        var body = Body(model, picture, handler);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        var details = Details(model);
        Grid.SetRow(details, 2);
        grid.Children.Add(details);
        return grid;
    }

    private UIElement Body(PreviewModel model, ImageSource? picture, Guid? handler)
    {
        if (model.Kind == PreviewKind.Document && handler is { } clsid && model.Path is { } path)
        {
            var host = new PreviewHandlerHost(clsid, path) { Height = BodyHeight };
            var holder = new Border { Child = host };
            host.Failed += (_, _) =>
            {
                _log.Warn($"Önizleyici açılamadı: {path}");
                if (ReferenceEquals(_handlerHost, host))
                {
                    _handlerHost = null;
                    holder.Child = Message("Bu belge için Windows önizleyicisi açılamadı");
                    host.Dispose();
                }
            };
            host.PreviewShown += (_, _) => DocumentShown?.Invoke(this, EventArgs.Empty);
            _handlerHost = host;
            return holder;
        }

        if (picture is not null)
        {
            return new Image
            {
                Source = picture,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxHeight = BodyHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        if (model.Text is { } text)
        {
            var monospace = model.Kind is PreviewKind.Text or PreviewKind.Folder;
            var block = new TextBlock
            {
                Text = model.Truncated ? text + (model.Kind == PreviewKind.Folder ? "\n…" : $"\n\n… (ilk {PreviewBuilder.MaxTextLines} satır)") : text,
                FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : new FontFamily("Segoe UI"),
                FontSize = monospace ? 12 : 14,
                TextWrapping = monospace ? TextWrapping.NoWrap : TextWrapping.Wrap,
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            return new ScrollViewer
            {
                Content = block,
                Height = BodyHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = monospace ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Focusable = false,
            };
        }

        return new Border { Height = 0 };
    }

    private static UIElement Details(PreviewModel model)
    {
        var panel = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < model.Details.Count; i++)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (label, value) = model.Details[i];
            var name = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 1, 12, 1) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            var text = new TextBlock { Text = value, FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value };
            text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            Grid.SetRow(name, i);
            Grid.SetRow(text, i);
            Grid.SetColumn(text, 1);
            panel.Children.Add(name);
            panel.Children.Add(text);
        }

        return panel;
    }

    private static UIElement Message(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return block;
    }
}
