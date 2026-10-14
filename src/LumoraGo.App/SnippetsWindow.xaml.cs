using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LumoraGo.Core.Search;
using LumoraGo.Core.Snippets;

namespace LumoraGo.App;

/// <summary>
/// Hazır metinleri ekleme/düzenleme ekranı: solda kategorilere ayrılmış liste ve arama, sağda ad, kısa ad, kategori,
/// içerik, yer tutucu düğmeleri ve canlı önizleme.
/// </summary>
internal partial class SnippetsWindow : Window
{
    private readonly SnippetStore _store;
    private readonly Func<string?> _clipboard;
    private Guid? _editing;
    private bool _loading;
    private bool _dirty;

    public sealed record Row(Guid Id, string Name, string Keyword, string Category, string Preview);

    public SnippetsWindow(SnippetStore store, Func<string?> clipboard, Guid? select)
    {
        InitializeComponent();
        Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("WindowBackgroundOpaqueColor"));
        _store = store;
        _clipboard = clipboard;

        foreach (var (token, description) in SnippetExpander.Placeholders)
        {
            var button = new Button { Content = token, ToolTip = description, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) };
            button.Click += (_, _) => InsertToken(token);
            PlaceholderPanel.Children.Add(button);
        }

        var fromClipboard = new Button { Content = "Panodan doldur", ToolTip = "Panodaki metni içeriğe koy", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) };
        fromClipboard.Click += (_, _) =>
        {
            if (_clipboard() is { Length: > 0 } text)
            {
                ContentBox.Text = text;
            }
        };
        PlaceholderPanel.Children.Add(fromClipboard);

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.New, Key.N, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => New_OnClick(this, new RoutedEventArgs())));

        Refresh(select);
        if (_store.All.Count == 0)
        {
            StartNew();
        }
    }

    /// <summary>Launcher'dan "düzenle" ile açılınca ilgili metni seçer.</summary>
    public void Select(Guid? id)
    {
        if (id is { } value && ConfirmDiscard())
        {
            Refresh(value);
        }
    }

    private void Refresh(Guid? select)
    {
        var folded = FuzzyMatcher.Fold(SearchBox.Text.Trim());
        var rows = _store.All
            .Where(s => folded.Length == 0
                || FuzzyMatcher.Fold(s.Name + " " + s.Keyword + " " + s.Category + " " + s.Content).Contains(folded, StringComparison.Ordinal))
            .Select(s => new Row(s.Id, s.Name, s.Keyword, s.Category.Length == 0 ? "Kategorisiz" : s.Category, OneLine(s.Content)))
            .ToList();
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Row.Category)));
        _loading = true;
        SnippetList.ItemsSource = view;
        CategoryBox.ItemsSource = _store.Categories;
        SnippetList.SelectedItem = rows.FirstOrDefault(r => r.Id == select);
        _loading = false;
        if (select is { } id && _store.Get(id) is { } snippet)
        {
            Load(snippet);
            SnippetList.ScrollIntoView(SnippetList.SelectedItem);
        }
    }

    private static string OneLine(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        return line.Length > 60 ? line[..60] + "…" : line;
    }

    private void Load(Snippet snippet)
    {
        _loading = true;
        _editing = snippet.Id;
        NameBox.Text = snippet.Name;
        KeywordBox.Text = snippet.Keyword;
        CategoryBox.Text = snippet.Category;
        ContentBox.Text = snippet.Content;
        UsageText.Text = $"Son düzenleme: {snippet.UpdatedAt:dd.MM.yyyy HH:mm} · {snippet.UseCount} kez kullanıldı"
            + (snippet.LastUsed is { } used ? $" · son kullanım {used:dd.MM.yyyy HH:mm}" : "");
        _loading = false;
        _dirty = false;
        DeleteButton.IsEnabled = true;
        UpdatePreview();
    }

    private void StartNew(Snippet? template = null)
    {
        _loading = true;
        _editing = null;
        SnippetList.SelectedItem = null;
        NameBox.Text = template is null ? "" : template.Name + " (kopya)";
        KeywordBox.Text = "";
        CategoryBox.Text = template?.Category ?? "";
        ContentBox.Text = template?.Content ?? "";
        UsageText.Text = "Yeni hazır metin";
        _loading = false;
        _dirty = template is not null;
        DeleteButton.IsEnabled = false;
        UpdatePreview();
        NameBox.Focus();
    }

    private Snippet Current() =>
        new(_editing ?? Guid.NewGuid(), NameBox.Text, KeywordBox.Text, ContentBox.Text, CategoryBox.Text, DateTime.Now);

    private void UpdatePreview()
    {
        var expanded = SnippetExpander.Expand(ContentBox.Text, DateTime.Now, _clipboard());
        PreviewText.Text = expanded.CharsAfterCursor > 0
            ? expanded.Text.Insert(expanded.Text.Length - expanded.CharsAfterCursor, "│")
            : expanded.Text;

        var keyword = SnippetStore.NormalizeKeyword(KeywordBox.Text);
        var error = _store.Validate(Current());
        KeywordNote.Text = error is not null && (NameBox.Text.Length > 0 || ContentBox.Text.Length > 0) ? "⚠ " + error
            : keyword.Length > 0 ? $"Launcher'a \"{keyword}\" yazıp Enter'a bas: önceki pencereye yapıştırılır. Ctrl+Enter panoya kopyalar."
            : "Kısa ad vermezsen \"/metin ad\" ile bulunur.";
        SaveButton.IsEnabled = error is null;
        StatusText.Text = _dirty ? "Kaydedilmedi" : "";
    }

    private void InsertToken(string token)
    {
        var index = ContentBox.CaretIndex;
        ContentBox.SelectedText = token;
        ContentBox.CaretIndex = index + token.Length;
        ContentBox.Focus();
    }

    private bool Save()
    {
        var snippet = Current();
        if (_store.Validate(snippet) is { } error)
        {
            StatusText.Text = error;
            return false;
        }

        var saved = _store.Save(snippet);
        _dirty = false;
        SearchBox.Text = "";
        Refresh(saved.Id);
        StatusText.Text = "Kaydedildi ✓";
        return true;
    }

    /// <returns>Devam edilebilirse true (kaydedildi, atıldı ya da değişiklik yoktu).</returns>
    private bool ConfirmDiscard()
    {
        if (!_dirty)
        {
            return true;
        }

        var answer = MessageBox.Show(this, "Değişiklikler kaydedilsin mi?", "Hazır metinler", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => !(_dirty = false),
            _ => false,
        };
    }

    private void Field_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _dirty = true;
        UpdatePreview();
    }

    private void SnippetList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SnippetList.SelectedItem is not Row row || row.Id == _editing)
        {
            return;
        }

        if (!ConfirmDiscard())
        {
            _loading = true;
            SnippetList.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            _loading = false;
            return;
        }

        if (_store.Get(row.Id) is { } snippet)
        {
            Load(snippet);
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) => Refresh(_editing);

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscard())
        {
            StartNew();
        }
    }

    private void Duplicate_OnClick(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscard())
        {
            StartNew(_editing is { } id ? _store.Get(id) : Current());
        }
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } id || _store.Get(id) is not { } snippet)
        {
            return;
        }

        if (MessageBox.Show(this, $"\"{snippet.Name}\" silinsin mi?", "Hazır metinler", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _store.Remove(id);
        _dirty = false;
        Refresh(null);
        StartNew();
        StatusText.Text = "Silindi";
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => Save();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "hazir-metinler.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, _store.Export());
            StatusText.Text = $"{_store.All.Count} metin dışa aktarıldı";
        }
    }

    private void Import_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var count = _store.Import(File.ReadAllText(dialog.FileName));
            Refresh(_editing);
            StatusText.Text = $"{count} metin içe aktarıldı";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            StatusText.Text = "Dosya okunamadı: " + ex.Message;
        }
    }
}
