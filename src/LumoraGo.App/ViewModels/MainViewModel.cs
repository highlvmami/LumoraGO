using LumoraGo.Core;
using LumoraGo.Plugin;

namespace LumoraGo.App.ViewModels;

internal sealed class MainViewModel(SearchService search, ILog log) : ObservableObject
{
    private string _queryText = "";
    private IReadOnlyList<Result> _results = [];
    private int _selectedIndex = -1;
    private CancellationTokenSource? _searchCts;

    public string QueryText
    {
        get => _queryText;
        set
        {
            if (SetProperty(ref _queryText, value))
            {
                _ = RunSearchAsync(value);
            }
        }
    }

    public IReadOnlyList<Result> Results
    {
        get => _results;
        private set
        {
            if (SetProperty(ref _results, value))
            {
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public bool HasResults => _results.Count > 0;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    public void MoveSelection(int delta) =>
        SelectedIndex = SelectionNavigator.Move(SelectedIndex, delta, Results.Count);

    /// <returns>Pencere gizlenmeli mi.</returns>
    public bool ExecuteSelected(ActionModifiers modifiers)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            return false;
        }

        var result = Results[SelectedIndex];
        try
        {
            return result.Action(new ActionContext(modifiers));
        }
        catch (Exception ex)
        {
            log.Error($"Eylem hata verdi: {result.PluginId} \"{result.Title}\": {ex}");
            return false;
        }
    }

    private async Task RunSearchAsync(string text)
    {
        // Her tuşta önceki aramayı iptal et; sadece en son yazılanın sonucu ekrana gelir.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = _searchCts = new CancellationTokenSource();

        try
        {
            var response = await search.SearchAsync(text, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Results = response.Results;
            SelectedIndex = response.Results.Count > 0 ? 0 : -1;
            log.Debug($"Sorgu \"{text}\": {response.Results.Count} sonuç, {response.Elapsed.TotalMilliseconds:F1} ms");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error($"Arama hata verdi: {ex}");
        }
    }
}
