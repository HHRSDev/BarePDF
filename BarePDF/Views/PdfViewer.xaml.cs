using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BarePDF.Pdfium;
using BarePDF.Settings;

namespace BarePDF.Views;

public partial class PdfViewer : UserControl
{
    private const double ZoomStep = 1.25;
    private const double MinScale = 0.1;
    private const double MaxScale = 8.0;

    private PdfDocument? _document;
    private CancellationTokenSource? _renderCts;
    private readonly HashSet<int> _renderingPages = new();
    private readonly Dictionary<int, PdfTextPage> _textPageCache = new();

    private PdfPageItem? _selectionPage;
    private PdfTextPage? _selectionTextPage;
    private int _selectionAnchor = -1;
    private bool _isSelecting;

    private ZoomMode _zoomMode = ZoomMode.FitPageHeight;
    private double _zoomScale = 1.0;

    private readonly List<(int pageIndex, int charIndex, int charCount)> _findResults = new();
    private int _currentMatchIndex = -1;
    private DispatcherTimer? _searchDebounce;

    public PdfViewer()
    {
        InitializeComponent();
        SizeChanged += OnViewerSizeChanged;

        FindBar.QueryChanged += _ => ScheduleSearch();
        FindBar.OptionsChanged += ScheduleSearch;
        FindBar.NavigateNext += OnFindNext;
        FindBar.NavigatePrev += OnFindPrev;
        FindBar.CloseRequested += HideFindBar;
    }

    public void ShowFindBar()
    {
        if (_document is null) return;
        FindBar.Visibility = Visibility.Visible;
        FindBar.Focus();
    }

    public void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ClearFindResults();
    }

    public bool HasDocument => _document is not null;
    public ZoomMode ZoomMode => _zoomMode;
    public double ZoomScale => _zoomScale;

    public double FirstPageDisplayWidth =>
        PageList.ItemsSource is IList<PdfPageItem> items && items.Count > 0
            ? items[0].DisplayWidth
            : 0;

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
        _zoomMode = settings.LastZoomMode ?? ZoomMode.FitPageHeight;
        if (_zoomMode == ZoomMode.Custom && settings.LastZoomScale is { } savedScale)
        {
            _zoomScale = Math.Clamp(savedScale, MinScale, MaxScale);
        }

        var document = await Task.Run(() => PdfDocument.Open(path));
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
            var firstWidth = sizes[0].w * 96.0 / 72.0;
            var firstHeight = sizes[0].h * 96.0 / 72.0;
            _zoomScale = ComputeFitScaleForSize(_zoomMode, firstWidth, firstHeight);
        }

        var items = new ObservableCollection<PdfPageItem>();
        for (int i = 0; i < sizes.Length; i++)
        {
            var item = new PdfPageItem(pageNumber: i + 1, sizes[i].w, sizes[i].h);
            item.Scale = _zoomScale;
            items.Add(item);
        }
        PageList.ItemsSource = items;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_document is null) return;
            _zoomScale = ComputeFitScale(_zoomMode);
            ApplyScaleToItems();
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
        var preview = new PrintPreviewWindow(_document, owner);
        preview.ShowDialog();
    }

    public void Print(Window owner)
    {
        if (_document is null) return;

        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            var pageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            var paginator = new PdfPrintPaginator(_document, pageSize);
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

    public void Close()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        _renderingPages.Clear();

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

        var textPage = GetOrLoadTextPage(item.PageNumber - 1);
        if (textPage is null) return;

        var pos = e.GetPosition(fe);
        var (pdfX, pdfY) = ToPdfCoords(item, pos);
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
        var dpi = 96.0 * _zoomScale;
        var capturedScale = _zoomScale;

        _ = Task.Run(() =>
        {
            try
            {
                if (token.IsCancellationRequested) return;
                using var page = doc.GetPage(index);
                if (token.IsCancellationRequested) return;
                var bitmap = page.Render(dpi);
                Dispatcher.InvokeAsync(() =>
                {
                    _renderingPages.Remove(index);
                    if (token.IsCancellationRequested) return;
                    if (Math.Abs(item.Scale - capturedScale) < 0.001)
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
