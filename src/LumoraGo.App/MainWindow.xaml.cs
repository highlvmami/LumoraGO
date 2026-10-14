using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LumoraGo.App.Controls;
using LumoraGo.App.Services;
using LumoraGo.App.ViewModels;
using LumoraGo.Core;
using LumoraGo.Core.Plugins;
using LumoraGo.Plugin;

namespace LumoraGo.App;

/// <summary>
/// Arama penceresi. Uygulama boyunca bir kez oluşturulur; kapatılmaz, sadece gizlenir. Hızlı açılmanın sırrı bu.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ILog _log;
    private readonly IntPtr _hwnd;
    private bool _allowClose;
    private readonly PreviewPane _preview;
    private readonly DispatcherTimer _previewDelay;
    private readonly DispatcherTimer _thumbnailRefresh;

    /// <summary>"/foto" yazılınca önizleme kendiliğinden açıldıysa true; aramadan çıkınca yine kendiliğinden kapanır.</summary>
    private bool _autoPreview;
    private bool _inPhotos;
    private const double PreviewWidth = 420;

    // Sürükle-bırak: farenin basıldığı yer ve satır; sürükleme sürerken pencere odak kaybedince kapanmaz.
    private Point? _dragStart;
    private Result? _dragResult;
    private bool _dragging;

    /// <summary>Ayarlardan: launcher her açıldığında önizleme de açık gelsin.</summary>
    public bool PreviewAlwaysOpen { get; set; }

    /// <summary>Marka yazısının altında küçük harflerle görünen sürüm.</summary>
    public string VersionText
    {
        set => BrandVersion.Text = value.Length > 0 ? "v" + value : "";
    }

    public bool IsPreviewOpen => PreviewColumn.Visibility == Visibility.Visible;

    internal MainWindow(MainViewModel viewModel, ILog log, ThemeService theme)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _log = log;
        DataContext = viewModel;

        if (!WindowEffects.SupportsBackdrop)
        {
            // Akrilik yoksa pencerenin kendisi opak boyanır; yuvarlak köşe de yalnızca Windows 11'de DWM'den gelir.
            SetResourceReference(BackgroundProperty, "WindowBackground");
            Root.CornerRadius = new CornerRadius(0);
        }

        // Pencere tutamacını göstermeden oluştur; ilk açılış da hızlı olsun.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowEffects.Apply(_hwnd, theme.IsDark);
        theme.Changed += (_, _) => WindowEffects.SetDarkMode(_hwnd, theme.IsDark);

        _preview = new PreviewPane(log);
        _preview.DocumentShown += (_, _) => QueryBox.Focus();
        PreviewColumn.Child = _preview;
        // Seçim hızla değişirken (↑↓ basılı) her satır için dosya okunmasın.
        _previewDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            _previewDelay!.Stop();
            if (IsPreviewOpen)
            {
                _preview.Show(_viewModel.SelectedResult);
            }
        }, Dispatcher);
        _previewDelay.Stop();

        // Küçük resimler arka planda hazırlanır; peş peşe gelenler tek yenilemede çizilsin.
        _thumbnailRefresh = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
        {
            _thumbnailRefresh!.Stop();
            RefreshResults(PhotosPlugin.Keyword);
        }, Dispatcher);
        _thumbnailRefresh.Stop();
        IconCache.ThumbnailLoaded += () => Dispatcher.BeginInvoke(() =>
        {
            if (!_thumbnailRefresh.IsEnabled)
            {
                _thumbnailRefresh.Start();
            }
        });

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.QueryText))
            {
                UpdatePhotoPreview(viewModel.QueryText);
            }

            if (e.PropertyName == nameof(MainViewModel.SelectedIndex) && viewModel.SelectedIndex >= 0)
            {
                ResultsList.ScrollIntoView(ResultsList.Items[viewModel.SelectedIndex]);
            }

            if (e.PropertyName is nameof(MainViewModel.SelectedIndex) or nameof(MainViewModel.Results) && IsPreviewOpen)
            {
                _previewDelay.Stop();
                _previewDelay.Start();
            }
        };
    }

    public void ToggleLauncher(Stopwatch? sinceHotkey = null)
    {
        if (IsVisible && IsActive)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher(sinceHotkey);
        }
    }

    public void ShowLauncher() => ShowLauncher(null);

    private void ShowLauncher(Stopwatch? sinceHotkey)
    {
        PositionOnCursorScreen();
        Show();
        Activate();
        if (!ForegroundHelper.ForceForeground(_hwnd))
        {
            _log.Warn("Pencere öne alınamadı (SetForegroundWindow reddedildi).");
        }

        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        // Önceki aramayı seçili bırak: yazmaya başlayınca yerine geçer.
        QueryBox.SelectAll();
        if (PreviewAlwaysOpen && !IsPreviewOpen)
        {
            SetPreview(true);
        }

        UpdatePhotoPreview(_viewModel.QueryText);

        PlayShowAnimation();

        if (sinceHotkey is not null)
        {
            // Render önceliğinden sonra çalışır, yani pencere çizildikten sonra ölçer.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                _log.Info($"Kısayoldan görünmeye: {sinceHotkey.Elapsed.TotalMilliseconds:F1} ms"));
        }
    }

    public void HideLauncher()
    {
        _viewModel.CloseMenu();
        if (IsPreviewOpen)
        {
            SetPreview(false);
        }

        _inPhotos = _autoPreview = false;
        Hide();
    }

    /// <summary>Önizleme panelini açar/kapatır; pencere sağa doğru genişler ama ortası yerinde kalır.</summary>
    private void SetPreview(bool open)
    {
        if (open == IsPreviewOpen)
        {
            return;
        }

        PreviewColumn.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Width += open ? PreviewWidth : -PreviewWidth;
        Left -= open ? PreviewWidth / 2 : -PreviewWidth / 2;
        if (open)
        {
            _preview.Show(_viewModel.SelectedResult);
        }
        else
        {
            _previewDelay.Stop();
            _preview.Clear();
        }

        QueryBox.Focus();
    }

    /// <summary>Hafif bir aşağı kayma ve belirme. Girdiyi bekletmez; yazı ilk karede kutuya gider.</summary>
    private void PlayShowAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(140);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 1, duration) { EasingFunction = ease });
        RootOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, duration) { EasingFunction = ease });
    }

    public void SetQuery(string text)
    {
        ShowLauncher();
        _viewModel.QueryText = text;
        QueryBox.CaretIndex = text.Length;
    }

    /// <summary>Kutudaki metin <paramref name="keyword"/> ile başlıyorsa sonuçları yeniden getirir.</summary>
    public void RefreshResults(string keyword) => _viewModel.Refresh(keyword);

    /// <summary>Fotoğraf aramasına girilince önizleme yanda kendiliğinden açılır, çıkınca kapanır.</summary>
    private void UpdatePhotoPreview(string text)
    {
        var inPhotos = text.StartsWith(PhotosPlugin.Keyword, StringComparison.OrdinalIgnoreCase);
        if (inPhotos == _inPhotos)
        {
            return;
        }

        _inPhotos = inPhotos;
        if (inPhotos && !IsPreviewOpen)
        {
            SetPreview(true);
            _autoPreview = true;
        }
        else if (!inPhotos && _autoPreview)
        {
            _autoPreview = false;
            if (!PreviewAlwaysOpen)
            {
                SetPreview(false);
            }
        }
    }

    internal void AllowClose()
    {
        _allowClose = true;
        Close();
    }

    private void PositionOnCursorScreen()
    {
        // Fare hangi ekrandaysa orada, ekranın üst kısmında ortala (PowerToys Run gibi).
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var area = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);

        var left = area.Left / dpi.DpiScaleX;
        var top = area.Top / dpi.DpiScaleY;
        var width = area.Width / dpi.DpiScaleX;
        var height = area.Height / dpi.DpiScaleY;

        Left = left + ((width - Width) / 2);
        Top = top + (height * 0.2);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_dragging)
        {
            HideLauncher();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space when Keyboard.Modifiers == ModifierKeys.Shift:
                _autoPreview = false;
                SetPreview(!IsPreviewOpen);
                e.Handled = true;
                break;
            case Key.Escape when IsPreviewOpen && !PreviewAlwaysOpen:
                _autoPreview = false;
                SetPreview(false);
                e.Handled = true;
                break;
            case Key.Escape when _viewModel.CloseMenu():
            case Key.Left when _viewModel.IsMenuOpen && _viewModel.CloseMenu():
                e.Handled = true;
                break;
            case Key.K when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.OpenMenu();
                e.Handled = true;
                break;
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
                // Odak başka yere geçmesin; Tab her zaman tamamlama içindir.
                if (_viewModel.TryComplete())
                {
                    QueryBox.CaretIndex = QueryBox.Text.Length;
                }

                e.Handled = true;
                break;
            case Key.Right when QueryBox.CaretIndex == QueryBox.Text.Length && QueryBox.SelectionLength == 0
                                && _viewModel.GhostSuffix.Length > 0:
                // Satır sonunda sağ ok, soluk öneriyi kabul eder (terminallerdeki gibi).
                _viewModel.TryComplete();
                QueryBox.CaretIndex = QueryBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Right when QueryBox.CaretIndex == QueryBox.Text.Length && QueryBox.SelectionLength == 0
                                && _viewModel.OpenMenu():
                // Öneri yoksa satır sonunda sağ ok, seçili sonucun eylem menüsünü açar.
                e.Handled = true;
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    private void ResultsList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragResult = null;
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ResultsList, source) is ListBoxItem { DataContext: Result result })
        {
            _dragStart = e.GetPosition(ResultsList);
            _dragResult = result;
        }
    }

    private void ResultsList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || _dragResult is not { } result)
        {
            return;
        }

        var moved = e.GetPosition(ResultsList) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        _dragResult = null;
        if (DragPayload.For(result) is not { } payload || Services.DragDataObject.Build(payload) is not { } drag)
        {
            return;
        }

        DragDropEffects effect;
        _dragging = true;
        try
        {
            effect = DragDrop.DoDragDrop(ResultsList, drag.Data, drag.Effects);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _log.Warn($"Sürükle-bırak başarısız: {ex.Message}");
            effect = DragDropEffects.None;
        }
        finally
        {
            _dragging = false;
        }

        e.Handled = true;
        if (effect != DragDropEffects.None)
        {
            // Bırakıldı: iş bitti, launcher kapanır.
            HideLauncher();
        }
        else
        {
            // Esc ya da geçersiz yere bırakma: launcher açık kalır ve odağı geri alır.
            Activate();
            QueryBox.Focus();
        }
    }

    private void ResultsList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragResult = null;
        // Sadece bir sonuca tıklanınca çalıştır, kaydırma çubuğuna değil.
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ResultsList, source) is ListBoxItem)
        {
            ExecuteSelected();
        }
    }

    private void ExecuteSelected()
    {
        if (_viewModel.ExecuteSelected(CurrentModifiers()))
        {
            HideLauncher();
        }
    }

    private static ActionModifiers CurrentModifiers()
    {
        var modifiers = ActionModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= ActionModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= ActionModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= ActionModifiers.Alt;
        return modifiers;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 pencereyi kapatmasın, gizlesin.
        if (!_allowClose)
        {
            e.Cancel = true;
            HideLauncher();
        }

        base.OnClosing(e);
    }
}
