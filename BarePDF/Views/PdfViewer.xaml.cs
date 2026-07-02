using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BarePDF.Pdfium;
using BarePDF.Settings;

namespace BarePDF.Views;

public partial class PdfViewer : UserControl
{
    private const double ZoomStep = 1.25;
    private const double MinScale = 0.1;
    private const double MaxScale = 8.0;

    private int _currentPageIndex;
    private bool _scrollSubscribed;
    private int _rotation;
    private string? _currentPath;
    private PageDisplayMode _displayMode = PageDisplayMode.Continuous;

    private PdfDocument? _document;
    private CancellationTokenSource? _renderCts;
    private readonly HashSet<int> _renderingPages = new();
    private readonly Dictionary<int, PdfTextPage> _textPageCache = new();

    private PdfPageItem? _selectionPage;
    private PdfTextPage? _selectionTextPage;
    private int _selectionAnchor = -1;
    private bool _isSelecting;

    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHOffset;
    private double _panStartVOffset;

    public static readonly DependencyProperty IsPanModeProperty =
        DependencyProperty.Register(nameof(IsPanMode), typeof(bool), typeof(PdfViewer),
            new PropertyMetadata(false, OnIsPanModeChanged));

    public bool IsPanMode
    {
        get => (bool)GetValue(IsPanModeProperty);
        set => SetValue(IsPanModeProperty, value);
    }

    public event EventHandler? PanModeChanged;

    private static void OnIsPanModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewer v) v.PanModeChanged?.Invoke(v, EventArgs.Empty);
    }

    public void TogglePanMode() => IsPanMode = !IsPanMode;

    private ZoomMode _zoomMode = ZoomMode.FitPage;
    private double _zoomScale = 1.0;

    private readonly List<(int pageIndex, int charIndex, int charCount)> _findResults = new();
    private int _currentMatchIndex = -1;
    private DispatcherTimer? _searchDebounce;

    private const double ThumbnailDpi = 32.0;
    private readonly HashSet<int> _renderingThumbnails = new();
    private List<ThumbnailItem>? _thumbnailItems;

    public PdfViewer()
    {
        InitializeComponent();
        SizeChanged += OnViewerSizeChanged;

        FindBar.QueryChanged += _ => ScheduleSearch();
        FindBar.OptionsChanged += ScheduleSearch;
        FindBar.NavigateNext += OnFindNext;
        FindBar.NavigatePrev += OnFindPrev;
        FindBar.CloseRequested += HideFindBar;

        Thumbnails.ThumbnailRealized += EnsureThumbnailRendered;
        Thumbnails.PageRequested += GoToPage;
    }

    public bool ThumbnailsVisible => Thumbnails.Visibility == Visibility.Visible;

    public void SetThumbnailsVisible(bool visible)
    {
        Thumbnails.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var settings = SettingsStore.Load();
        if ((settings.ShowThumbnails ?? false) != visible)
        {
            settings.ShowThumbnails = visible;
            SettingsStore.Save(settings);
        }
    }

    public void ToggleThumbnails() => SetThumbnailsVisible(!ThumbnailsVisible);

    public void GoToPage(int pageIndex)
    {
        if (PageList.ItemsSource is not IList<PdfPageItem> items) return;
        if (pageIndex < 0 || pageIndex >= items.Count) return;
        var target = items[pageIndex];

        if (_displayMode == PageDisplayMode.SinglePage)
        {
            // Other items are Collapsed, so ScrollIntoView won't work. Set the index
            // explicitly (UpdateSinglePageVisibility swaps which page is visible) and
            // reset the scroll to the top of the new page.
            CurrentPageIndex = pageIndex;
            GetListScrollViewer()?.ScrollToHome();
        }
        else
        {
            PageList.ScrollIntoView(target);
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (target.Image is null) EnsureRendered(target);
        }, DispatcherPriority.Loaded);
    }

    public void ScrollPageDown()
    {
        if (_displayMode == PageDisplayMode.SinglePage) GoToNextPage();
        else GetListScrollViewer()?.PageDown();
    }
    public void ScrollPageUp()
    {
        if (_displayMode == PageDisplayMode.SinglePage) GoToPreviousPage();
        else GetListScrollViewer()?.PageUp();
    }
    public void ScrollToFirstPage() => GoToPage(0);
    public void ScrollToLastPage()
    {
        if (PageCount > 0) GoToPage(PageCount - 1);
    }

    public PageDisplayMode DisplayMode => _displayMode;

    public void SetDisplayMode(PageDisplayMode mode)
    {
        if (_displayMode == mode) return;
        _displayMode = mode;
        ApplyDisplayMode();

        var settings = SettingsStore.Load();
        settings.PageDisplayMode = mode;
        SettingsStore.Save(settings);
    }

    private void ApplyDisplayMode()
    {
        if (PageList.ItemsSource is not IList<PdfPageItem> items) return;
        if (_displayMode == PageDisplayMode.SinglePage)
        {
            UpdateSinglePageVisibility();
        }
        else
        {
            foreach (var item in items)
            {
                item.ItemVisibility = Visibility.Visible;
            }
        }
    }

    private void OnPageListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_displayMode != PageDisplayMode.SinglePage) return;
        var sv = GetListScrollViewer();
        if (sv is null) return;

        const double edgeTolerance = 0.5;
        var atTop = sv.VerticalOffset <= edgeTolerance;
        var atBottom = sv.VerticalOffset >= sv.ScrollableHeight - edgeTolerance;

        if (e.Delta > 0 && atTop && CurrentPageIndex > 0)
        {
            GoToPreviousPage();
            // Land at the bottom of the previous page so wheeling up reads naturally.
            Dispatcher.InvokeAsync(() => GetListScrollViewer()?.ScrollToEnd(),
                DispatcherPriority.Loaded);
            e.Handled = true;
        }
        else if (e.Delta < 0 && atBottom && CurrentPageIndex < PageCount - 1)
        {
            GoToNextPage();
            e.Handled = true;
        }
    }

    private void UpdateSinglePageVisibility()
    {
        if (_displayMode != PageDisplayMode.SinglePage) return;
        if (PageList.ItemsSource is not IList<PdfPageItem> items) return;
        var current = CurrentPageIndex;
        for (int i = 0; i < items.Count; i++)
        {
            items[i].ItemVisibility = i == current ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void RotateRight() => ApplyRotation((_rotation + 1) % 4);
    public void RotateLeft() => ApplyRotation((_rotation + 3) % 4);

    private void ApplyRotation(int rotation)
    {
        if (_document is null) return;
        rotation = ((rotation % 4) + 4) % 4;
        if (_rotation == rotation) return;
        _rotation = rotation;

        if (PageList.ItemsSource is IList<PdfPageItem> items)
        {
            foreach (var item in items)
            {
                item.Rotation = _rotation;
                item.Image = null;
            }
        }

        // Rotation changes effective page dimensions; recompute fit and re-render visible items.
        if (_zoomMode != ZoomMode.Custom)
        {
            _zoomScale = ComputeFitScale(_zoomMode);
        }
        ApplyScaleToItems();

        PersistRotation();
    }

    private void PersistRotation()
    {
        if (_currentPath is null) return;
        var settings = SettingsStore.Load();
        var map = settings.PerDocumentRotation ??= new Dictionary<string, int>();
        if (_rotation == 0) map.Remove(_currentPath);
        else map[_currentPath] = _rotation;
        SettingsStore.Save(settings);
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        private set
        {
            var clamped = Math.Max(0, Math.Min(value, Math.Max(0, PageCount - 1)));
            if (_currentPageIndex == clamped) return;
            _currentPageIndex = clamped;
            UpdatePageIndicator();
            UpdateSinglePageVisibility();
        }
    }

    public void GoToPreviousPage()
    {
        if (CurrentPageIndex > 0) GoToPage(CurrentPageIndex - 1);
    }

    public void GoToNextPage()
    {
        if (CurrentPageIndex < PageCount - 1) GoToPage(CurrentPageIndex + 1);
    }

    private void UpdatePageIndicator()
    {
        var total = PageCount;
        if (total == 0)
        {
            PageIndicator.Text = "0 / 0";
            PageJumpBox.Text = string.Empty;
            return;
        }
        var n = _currentPageIndex + 1;
        PageIndicator.Text = $"{n} / {total}";
        if (!PageJumpBox.IsKeyboardFocused)
        {
            PageJumpBox.Text = n.ToString();
        }
    }

    private void UpdateCurrentPageFromScroll()
    {
        if (PageList.ItemsSource is not IList<PdfPageItem> items || items.Count == 0) return;
        var sv = GetListScrollViewer();
        if (sv is null) return;

        var center = sv.VerticalOffset + sv.ViewportHeight / 2.0;
        const double topPadding = 20.0;
        const double itemMargin = 12.0;
        var accum = topPadding;
        for (int i = 0; i < items.Count; i++)
        {
            var h = items[i].DisplayHeight;
            if (center <= accum + h + itemMargin / 2.0)
            {
                CurrentPageIndex = i;
                return;
            }
            accum += h + itemMargin;
        }
        CurrentPageIndex = items.Count - 1;
    }

    private void OnPageListScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ExtentHeightChange != 0)
        {
            UpdateCurrentPageFromScroll();
        }
    }

    private void OnRibbonFirstPage(object sender, RoutedEventArgs e) => ScrollToFirstPage();
    private void OnRibbonPrevPage(object sender, RoutedEventArgs e) => GoToPreviousPage();
    private void OnRibbonNextPage(object sender, RoutedEventArgs e) => GoToNextPage();
    private void OnRibbonLastPage(object sender, RoutedEventArgs e) => ScrollToLastPage();

    private void OnPageJumpKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPageJump();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdatePageIndicator();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnPageJumpLostFocus(object sender, RoutedEventArgs e) => UpdatePageIndicator();

    private void CommitPageJump()
    {
        if (int.TryParse(PageJumpBox.Text, out var n) && n >= 1 && n <= PageCount)
        {
            GoToPage(n - 1);
            CurrentPageIndex = n - 1;
        }
        else
        {
            System.Media.SystemSounds.Beep.Play();
            UpdatePageIndicator();
        }
    }

    private ScrollViewer? GetListScrollViewer() => FindVisualChild<ScrollViewer>(PageList);

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void EnsureThumbnailRendered(ThumbnailItem item)
    {
        if (item.Thumbnail is not null) return;
        if (_document is null) return;

        var index = item.PageNumber - 1;
        if (!_renderingThumbnails.Add(index)) return;

        var doc = _document;
        var token = _renderCts?.Token ?? CancellationToken.None;

        _ = Task.Run(() =>
        {
            try
            {
                if (token.IsCancellationRequested) return;
                using var page = doc.GetPage(index);
                if (token.IsCancellationRequested) return;
                var bitmap = page.Render(ThumbnailDpi);
                Dispatcher.InvokeAsync(() =>
                {
                    _renderingThumbnails.Remove(index);
                    if (token.IsCancellationRequested) return;
                    item.Thumbnail = bitmap;
                });
            }
            catch
            {
                Dispatcher.InvokeAsync(() => _renderingThumbnails.Remove(index));
            }
        }, token);
    }

    public void ShowFindBar()
    {
        if (_document is null) return;
        FindBar.Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(() => FindBar.Focus(), DispatcherPriority.Loaded);
    }

    public void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ClearFindResults();
    }

    public bool HasDocument => _document is not null;
    public PdfDocument? Document => _document;
    public string? CurrentPath => _currentPath;
    public ZoomMode ZoomMode => _zoomMode;
    public double ZoomScale => _zoomScale;

    public double FirstPageDisplayWidth =>
        PageList.ItemsSource is IList<PdfPageItem> items && items.Count > 0
            ? items[0].DisplayWidth
            : 0;

    public double FirstPageDisplayHeight =>
        PageList.ItemsSource is IList<PdfPageItem> items2 && items2.Count > 0
            ? items2[0].DisplayHeight
            : 0;

    public int PageCount =>
        PageList.ItemsSource is IList<PdfPageItem> list ? list.Count : 0;

    public bool HasSelection
    {
        get
        {
            if (PageList.ItemsSource is not IEnumerable<PdfPageItem> items) return false;
            foreach (var item in items)
            {
                if (item.SelectionStart >= 0 && item.SelectionEnd >= item.SelectionStart) return true;
            }
            return false;
        }
    }

    public string GetSelectedText()
    {
        if (PageList.ItemsSource is not IEnumerable<PdfPageItem> items) return string.Empty;
        foreach (var item in items)
        {
            if (item.SelectionStart < 0 || item.SelectionEnd < item.SelectionStart) continue;
            var textPage = GetOrLoadTextPage(item.PageNumber - 1);
            if (textPage is null) continue;
            return textPage.ExtractText(item.SelectionStart, item.SelectionEnd - item.SelectionStart + 1);
        }
        return string.Empty;
    }

    public void CopySelectedText()
    {
        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    public async Task OpenAsync(string path)
    {
        Close();

        var settings = SettingsStore.Load();
        _zoomMode = settings.LastZoomMode ?? ZoomMode.FitPage;
        if (_zoomMode == ZoomMode.Custom && settings.LastZoomScale is { } savedScale)
        {
            _zoomScale = Math.Clamp(savedScale, MinScale, MaxScale);
        }

        _currentPath = System.IO.Path.GetFullPath(path);
        _rotation = settings.PerDocumentRotation is { } map && map.TryGetValue(_currentPath, out var savedRot)
            ? ((savedRot % 4) + 4) % 4
            : 0;
        _displayMode = settings.PageDisplayMode ?? PageDisplayMode.Continuous;

        PdfDocument? document = null;
        string? attemptedPassword = null;
        var hasAttempted = false;
        while (document is null)
        {
            try
            {
                var pwd = attemptedPassword;
                document = await Task.Run(() => PdfDocument.Open(path, pwd));
            }
            catch (PdfException ex) when (ex.ErrorCode == 4)
            {
                var prompt = new PasswordPromptDialog(System.IO.Path.GetFileName(path), retry: hasAttempted)
                {
                    Owner = Window.GetWindow(this)
                };
                if (prompt.ShowDialog() != true)
                {
                    throw new OperationCanceledException("Password entry cancelled.");
                }
                attemptedPassword = prompt.Password;
                hasAttempted = true;
            }
        }

        _document = document;

        var sizes = await Task.Run(() =>
        {
            var list = new (double w, double h)[document.PageCount];
            for (int i = 0; i < document.PageCount; i++)
            {
                using var page = document.GetPage(i);
                list[i] = (page.WidthPoints, page.HeightPoints);
            }
            return list;
        });

        _renderCts = new CancellationTokenSource();

        if (_zoomMode != ZoomMode.Custom && sizes.Length > 0)
        {
            var swap = _rotation == 1 || _rotation == 3;
            var firstWidth = (swap ? sizes[0].h : sizes[0].w) * 96.0 / 72.0;
            var firstHeight = (swap ? sizes[0].w : sizes[0].h) * 96.0 / 72.0;
            _zoomScale = ComputeFitScaleForSize(_zoomMode, firstWidth, firstHeight);
        }

        var items = new ObservableCollection<PdfPageItem>();
        for (int i = 0; i < sizes.Length; i++)
        {
            var item = new PdfPageItem(pageNumber: i + 1, sizes[i].w, sizes[i].h);
            item.Rotation = _rotation;
            item.Scale = _zoomScale;
            items.Add(item);
        }
        PageList.ItemsSource = items;
        ApplyDisplayMode();

        _thumbnailItems = new List<ThumbnailItem>(sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            _thumbnailItems.Add(new ThumbnailItem(i + 1, sizes[i].w, sizes[i].h));
        }
        Thumbnails.SetItems(_thumbnailItems);
        Thumbnails.Visibility = (settings.ShowThumbnails ?? false)
            ? Visibility.Visible
            : Visibility.Collapsed;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_document is null) return;
            _zoomScale = ComputeFitScale(_zoomMode);
            ApplyScaleToItems();

            if (!_scrollSubscribed)
            {
                var sv = GetListScrollViewer();
                if (sv is not null)
                {
                    sv.ScrollChanged += OnPageListScrollChanged;
                    _scrollSubscribed = true;
                }
            }
            _currentPageIndex = 0;
            UpdatePageIndicator();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void SetZoomMode(ZoomMode mode)
    {
        _zoomMode = mode;
        var newScale = mode == ZoomMode.Custom
            ? Math.Clamp(_zoomScale, MinScale, MaxScale)
            : ComputeFitScale(mode);
        ChangeScale(newScale);
        PersistZoomState();
    }

    public void ZoomIn() => ZoomBy(ZoomStep);
    public void ZoomOut() => ZoomBy(1.0 / ZoomStep);

    private void ZoomBy(double factor)
    {
        var newScale = Math.Clamp(_zoomScale * factor, MinScale, MaxScale);
        _zoomMode = ZoomMode.Custom;
        ChangeScale(newScale);
        PersistZoomState();
    }

    private void PersistZoomState()
    {
        var settings = SettingsStore.Load();
        settings.LastZoomMode = _zoomMode;
        settings.LastZoomScale = _zoomMode == ZoomMode.Custom ? _zoomScale : null;
        SettingsStore.Save(settings);
    }

    private void ChangeScale(double newScale)
    {
        if (Math.Abs(newScale - _zoomScale) < 0.0001 &&
            _zoomMode != ZoomMode.Custom)
        {
            return;
        }

        _zoomScale = newScale;
        ApplyScaleToItems();
    }

    private void ApplyScaleToItems()
    {
        if (PageList.ItemsSource is not IEnumerable<PdfPageItem> items) return;
        foreach (var item in items)
        {
            item.Scale = _zoomScale;
            item.Image = null;
        }
        RerenderRealizedItems();
    }

    private void RerenderRealizedItems()
    {
        var generator = PageList.ItemContainerGenerator;
        for (int i = 0; i < PageList.Items.Count; i++)
        {
            if (generator.ContainerFromIndex(i) is ListBoxItem &&
                PageList.Items[i] is PdfPageItem item)
            {
                EnsureRendered(item);
            }
        }
    }

    private double ComputeFitScale(ZoomMode mode)
    {
        if (PageList.ItemsSource is not IList<PdfPageItem> items || items.Count == 0)
        {
            return 1.0;
        }
        var first = items[0];
        return ComputeFitScaleForSize(mode, first.WidthPoints * 96.0 / 72.0, first.HeightPoints * 96.0 / 72.0);
    }

    private double ComputeFitScaleForSize(ZoomMode mode, double pageWidthLogical, double pageHeightLogical)
    {
        var viewportWidth = Math.Max(0, PageList.ActualWidth - 60);
        var viewportHeight = Math.Max(0, PageList.ActualHeight - 40);

        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0;
        }

        return mode switch
        {
            ZoomMode.FitPage => Math.Min(viewportWidth / pageWidthLogical, viewportHeight / pageHeightLogical),
            ZoomMode.FitPageHeight => viewportHeight / pageHeightLogical,
            ZoomMode.FitWidth => viewportWidth / pageWidthLogical,
            ZoomMode.ActualSize => 1.0,
            _ => _zoomScale,
        };
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoomMode == ZoomMode.Custom || _document is null) return;
        var newScale = ComputeFitScale(_zoomMode);
        ChangeScale(newScale);
    }

    public void ShowPrintPreview(Window owner)
    {
        if (_document is null) return;
        try
        {
            var preview = new PrintPreviewWindow(_document, owner);
            preview.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner,
                $"Print Preview failed to open.\n\n{ex.GetType().Name}: {ex.Message}",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void Print(Window owner)
    {
        if (_document is null) return;

        var dialog = new System.Windows.Controls.PrintDialog
        {
            UserPageRangeEnabled = true,
            MinPage = 1,
            MaxPage = (uint)Math.Max(1, _document.PageCount),
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var pageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            var (firstPage, pageCount) = ResolveRange(dialog, _document.PageCount);
            var paginator = new PdfPrintPaginator(_document, pageSize, firstPage: firstPage, pageCount: pageCount);
            dialog.PrintDocument(paginator, "BarePDF Document");
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner,
                $"Could not print this PDF.\n\n{ex.Message}",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static (int firstPage, int pageCount) ResolveRange(
        System.Windows.Controls.PrintDialog dialog,
        int totalPages)
    {
        if (dialog.PageRangeSelection != System.Windows.Controls.PageRangeSelection.UserPages)
            return (0, totalPages);

        var from = Math.Max(1, dialog.PageRange.PageFrom);
        var to = Math.Min(totalPages, Math.Max(from, dialog.PageRange.PageTo));
        return (from - 1, to - from + 1);
    }

    public void Close()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        _renderingPages.Clear();
        _renderingThumbnails.Clear();
        _thumbnailItems = null;
        Thumbnails.SetItems(null);

        _isSelecting = false;
        _selectionPage = null;
        _selectionTextPage = null;
        _selectionAnchor = -1;

        _findResults.Clear();
        _currentMatchIndex = -1;
        _searchDebounce?.Stop();
        FindBar.Visibility = Visibility.Collapsed;

        foreach (var tp in _textPageCache.Values) tp.Dispose();
        _textPageCache.Clear();

        PageList.ItemsSource = null;

        _document?.Dispose();
        _document = null;

        _currentPageIndex = 0;
        UpdatePageIndicator();

        _rotation = 0;
        _currentPath = null;
    }

    private void ScheduleSearch()
    {
        _searchDebounce ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _searchDebounce.Tick -= OnSearchTick;
        _searchDebounce.Tick += OnSearchTick;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnSearchTick(object? sender, EventArgs e)
    {
        _searchDebounce?.Stop();
        RunSearch();
    }

    private void RunSearch()
    {
        var query = FindBar.Query;
        if (string.IsNullOrEmpty(query) || _document is null)
        {
            ClearFindResults();
            return;
        }

        _findResults.Clear();
        _currentMatchIndex = -1;

        if (PageList.ItemsSource is not IList<PdfPageItem> items)
        {
            FindBar.SetMatchInfo(0, 0);
            return;
        }

        for (int p = 0; p < items.Count; p++)
        {
            var textPage = GetOrLoadTextPage(p);
            if (textPage is null) continue;
            foreach (var m in textPage.FindAll(query, FindBar.CaseSensitive, FindBar.WholeWord))
            {
                _findResults.Add((p, m.CharIndex, m.CharCount));
            }
        }

        var perPage = new Dictionary<int, List<Rect>>();
        foreach (var (pageIdx, charIdx, charCnt) in _findResults)
        {
            var textPage = GetOrLoadTextPage(pageIdx);
            if (textPage is null) continue;
            var item = items[pageIdx];
            if (!perPage.TryGetValue(pageIdx, out var list))
            {
                list = new List<Rect>();
                perPage[pageIdx] = list;
            }
            foreach (var pdfRect in textPage.GetSelectionRects(charIdx, charCnt))
            {
                list.Add(PdfBoxToWpfRect(item, pdfRect));
            }
        }
        for (int p = 0; p < items.Count; p++)
        {
            items[p].MatchRects = perPage.TryGetValue(p, out var list) ? list : null;
            items[p].CurrentMatchRects = null;
        }

        if (_findResults.Count > 0)
        {
            _currentMatchIndex = 0;
            UpdateCurrentMatchHighlight();
            ScrollToCurrentMatch();
        }
        FindBar.SetMatchInfo(_currentMatchIndex, _findResults.Count);
    }

    private void ClearFindResults()
    {
        _findResults.Clear();
        _currentMatchIndex = -1;
        if (PageList.ItemsSource is IEnumerable<PdfPageItem> items)
        {
            foreach (var item in items)
            {
                item.MatchRects = null;
                item.CurrentMatchRects = null;
            }
        }
        FindBar.SetMatchInfo(0, 0);
    }

    private void UpdateCurrentMatchHighlight()
    {
        if (PageList.ItemsSource is not IList<PdfPageItem> items) return;
        foreach (var existing in items)
        {
            if (existing.CurrentMatchRects is not null) existing.CurrentMatchRects = null;
        }

        if (_currentMatchIndex < 0 || _currentMatchIndex >= _findResults.Count) return;
        var (pageIdx, charIdx, charCnt) = _findResults[_currentMatchIndex];
        var textPage = GetOrLoadTextPage(pageIdx);
        if (textPage is null) return;
        var pageItem = items[pageIdx];
        var rects = new List<Rect>();
        foreach (var pdfRect in textPage.GetSelectionRects(charIdx, charCnt))
        {
            rects.Add(PdfBoxToWpfRect(pageItem, pdfRect));
        }
        pageItem.CurrentMatchRects = rects;
    }

    private void ScrollToCurrentMatch()
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _findResults.Count) return;
        if (PageList.ItemsSource is not IList<PdfPageItem> items) return;
        var pageIdx = _findResults[_currentMatchIndex].pageIndex;
        if (pageIdx >= items.Count) return;
        var target = items[pageIdx];
        PageList.ScrollIntoView(target);
        Dispatcher.InvokeAsync(() =>
        {
            if (target.Image is null) EnsureRendered(target);
        }, DispatcherPriority.Loaded);
    }

    private void OnFindNext()
    {
        if (_findResults.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex + 1) % _findResults.Count;
        UpdateCurrentMatchHighlight();
        ScrollToCurrentMatch();
        FindBar.SetMatchInfo(_currentMatchIndex, _findResults.Count);
    }

    private void OnFindPrev()
    {
        if (_findResults.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex - 1 + _findResults.Count) % _findResults.Count;
        UpdateCurrentMatchHighlight();
        ScrollToCurrentMatch();
        FindBar.SetMatchInfo(_currentMatchIndex, _findResults.Count);
    }

    private PdfTextPage? GetOrLoadTextPage(int pageIndex)
    {
        if (_document is null) return null;
        if (_textPageCache.TryGetValue(pageIndex, out var cached)) return cached;
        try
        {
            using var page = _document.GetPage(pageIndex);
            var textPage = page.LoadTextPage();
            _textPageCache[pageIndex] = textPage;
            return textPage;
        }
        catch
        {
            return null;
        }
    }

    private static (double pdfX, double pdfY) ToPdfCoords(PdfPageItem item, Point wpf)
    {
        if (item.DisplayWidth <= 0 || item.DisplayHeight <= 0) return (0, 0);
        var pdfX = wpf.X * item.WidthPoints / item.DisplayWidth;
        var pdfY = item.HeightPoints - wpf.Y * item.HeightPoints / item.DisplayHeight;
        return (pdfX, pdfY);
    }

    private static Rect PdfBoxToWpfRect(PdfPageItem item, PdfRect box)
    {
        var sx = item.DisplayWidth / item.WidthPoints;
        var sy = item.DisplayHeight / item.HeightPoints;
        var x = box.Left * sx;
        var w = (box.Right - box.Left) * sx;
        var y = (item.HeightPoints - box.Top) * sy;
        var h = (box.Top - box.Bottom) * sy;
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        return new Rect(x, y, w, h);
    }

    private void UpdateSelection(PdfPageItem item, PdfTextPage textPage, int anchor, int caret)
    {
        var (start, end) = anchor < caret ? (anchor, caret) : (caret, anchor);
        item.SelectionStart = start;
        item.SelectionEnd = end;

        var pdfRects = textPage.GetSelectionRects(start, end - start + 1);
        var wpfRects = new List<Rect>(pdfRects.Count);
        foreach (var box in pdfRects)
        {
            wpfRects.Add(PdfBoxToWpfRect(item, box));
        }
        item.SelectedRects = wpfRects;
    }

    private void ClearAllSelections()
    {
        if (PageList.ItemsSource is not IEnumerable<PdfPageItem> items) return;
        foreach (var i in items)
        {
            if (i.SelectedRects is null) continue;
            i.SelectedRects = null;
            i.SelectionStart = -1;
            i.SelectionEnd = -1;
        }
    }

    private void OnPageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PdfPageItem item) return;
        if (_document is null) return;

        var pos = e.GetPosition(fe);
        var (pdfX, pdfY) = ToPdfCoords(item, pos);

        var linkTarget = GetLinkAtPoint(item, pdfX, pdfY);
        if (linkTarget is not null)
        {
            ClearAllSelections();
            NavigateLink(linkTarget);
            e.Handled = true;
            return;
        }

        var textPage = GetOrLoadTextPage(item.PageNumber - 1);
        if (textPage is null) return;

        var index = textPage.GetCharIndexAtPoint(pdfX, pdfY);

        ClearAllSelections();
        if (index < 0)
        {
            _isSelecting = false;
            return;
        }

        _selectionPage = item;
        _selectionTextPage = textPage;
        _selectionAnchor = index;
        _isSelecting = true;
        fe.CaptureMouse();
        UpdateSelection(item, textPage, index, index);
        e.Handled = true;
    }

    private PdfLinkTarget? GetLinkAtPoint(PdfPageItem item, double pdfX, double pdfY)
    {
        if (_document is null) return null;
        try
        {
            using var page = _document.GetPage(item.PageNumber - 1);
            return page.GetLinkAtPoint(pdfX, pdfY);
        }
        catch
        {
            return null;
        }
    }

    private void NavigateLink(PdfLinkTarget target)
    {
        switch (target)
        {
            case PdfUriLinkTarget uri:
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.Url) { UseShellExecute = true });
                }
                catch { /* shell may refuse some schemes — silently ignore */ }
                break;

            case PdfGoToLinkTarget goTo:
                if (PageList.ItemsSource is IList<PdfPageItem> items
                    && goTo.PageIndex >= 0 && goTo.PageIndex < items.Count)
                {
                    var target2 = items[goTo.PageIndex];
                    PageList.ScrollIntoView(target2);
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (target2.Image is null) EnsureRendered(target2);
                    }, DispatcherPriority.Loaded);
                }
                break;
        }
    }

    private void OnPageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting || _selectionPage is null || _selectionTextPage is null) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not PdfPageItem item) return;
        if (!ReferenceEquals(item, _selectionPage)) return;

        var pos = e.GetPosition(fe);
        var (pdfX, pdfY) = ToPdfCoords(item, pos);
        var index = _selectionTextPage.GetCharIndexAtPoint(pdfX, pdfY, xTolerance: 8.0, yTolerance: 8.0);
        if (index < 0) return;
        UpdateSelection(item, _selectionTextPage, _selectionAnchor, index);
    }

    private void OnPageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
        _isSelecting = false;
    }

    private void OnPageListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsPanMode) return;
        var sv = GetListScrollViewer();
        if (sv is null) return;

        _isPanning = true;
        _panStartPoint = e.GetPosition(PageList);
        _panStartHOffset = sv.HorizontalOffset;
        _panStartVOffset = sv.VerticalOffset;
        PageList.CaptureMouse();
        e.Handled = true;
    }

    private void OnPageListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var sv = GetListScrollViewer();
        if (sv is null) return;

        var current = e.GetPosition(PageList);
        var dx = current.X - _panStartPoint.X;
        var dy = current.Y - _panStartPoint.Y;

        sv.ScrollToHorizontalOffset(_panStartHOffset - dx);
        sv.ScrollToVerticalOffset(_panStartVOffset - dy);
        e.Handled = true;
    }

    private void OnPageListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        PageList.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnPageContainerDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PdfPageItem item)
        {
            EnsureRendered(item);
        }
    }

    private void EnsureRendered(PdfPageItem item)
    {
        if (item.Image is not null) return;
        if (_document is null) return;

        var index = item.PageNumber - 1;
        if (!_renderingPages.Add(index)) return;

        var doc = _document;
        var token = _renderCts?.Token ?? CancellationToken.None;
        // Multiply by the display's device-pixel scale so the rendered bitmap
        // has one pixel per physical screen pixel — otherwise WPF's bilinear
        // upscale to physical pixels softens text on 125/150/200% displays.
        var displayScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (displayScale <= 0) displayScale = 1.0;
        var dpi = 96.0 * _zoomScale * displayScale;
        var densityDpi = 96.0 * displayScale;
        var capturedScale = _zoomScale;
        var capturedRotation = _rotation;

        _ = Task.Run(() =>
        {
            try
            {
                if (token.IsCancellationRequested) return;
                using var page = doc.GetPage(index);
                if (token.IsCancellationRequested) return;
                var bitmap = page.Render(dpi, capturedRotation, densityDpi, useLcdText: true);
                Dispatcher.InvokeAsync(() =>
                {
                    _renderingPages.Remove(index);
                    if (token.IsCancellationRequested) return;
                    if (Math.Abs(item.Scale - capturedScale) < 0.001 && item.Rotation == capturedRotation)
                    {
                        item.Image = bitmap;
                    }
                    else
                    {
                        EnsureRendered(item);
                    }
                });
            }
            catch
            {
                Dispatcher.InvokeAsync(() => _renderingPages.Remove(index));
            }
        }, token);
    }
}
