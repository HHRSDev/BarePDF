using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BarePDF.Pdfium;

namespace BarePDF.Views;

public partial class PrintPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    private const double ZoomStep = 1.25;
    private const double MinScale = 0.1;
    private const double MaxScale = 4.0;

    private readonly PdfDocument _document;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<int> _rendering = new();
    private ObservableCollection<PdfPageItem>? _items;
    private ScrollViewer? _scroller;
    private double _scale = 1.0;
    private int _currentPageIndex;

    public PrintPreviewWindow(PdfDocument document, Window? owner = null)
    {
        _document = document;
        if (owner is not null) Owner = owner;
        InitializeComponent();

        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => _cts.Cancel();
        KeyDown += OnKeyDown;
    }

    private async Task InitializeAsync()
    {
        var sizes = await Task.Run(() =>
        {
            var list = new (double w, double h)[_document.PageCount];
            for (int i = 0; i < _document.PageCount; i++)
            {
                using var page = _document.GetPage(i);
                list[i] = (page.WidthPoints, page.HeightPoints);
            }
            return list;
        });

        if (sizes.Length > 0)
        {
            _scale = ComputeFitScale(sizes[0].w, sizes[0].h);
        }

        _items = new ObservableCollection<PdfPageItem>();
        for (int i = 0; i < sizes.Length; i++)
        {
            var item = new PdfPageItem(pageNumber: i + 1, sizes[i].w, sizes[i].h)
            {
                Scale = _scale,
            };
            _items.Add(item);
        }
        PageList.ItemsSource = _items;
        UpdateZoomIndicator();
        UpdatePageIndicator();

        await Dispatcher.InvokeAsync(HookScroller, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void HookScroller()
    {
        _scroller = FindVisualChild<ScrollViewer>(PageList);
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }
    }

    private double ComputeFitScale(double pageWidthPoints, double pageHeightPoints)
    {
        var inner = GetPageAreaSize();
        if (inner.Width <= 0 || inner.Height <= 0) return 1.0;
        const double padding = 48; // ListBox padding + page margin allowance
        var pageW = pageWidthPoints * 96.0 / 72.0;
        var pageH = pageHeightPoints * 96.0 / 72.0;
        var sx = (inner.Width - padding) / pageW;
        var sy = (inner.Height - padding) / pageH;
        var s = Math.Min(sx, sy);
        return Math.Clamp(s, MinScale, MaxScale);
    }

    private Size GetPageAreaSize()
    {
        // ActualWidth/Height before first layout pass can be 0; fall back to window.
        var w = PageList.ActualWidth > 0 ? PageList.ActualWidth : Math.Max(0, ActualWidth - 32);
        var h = PageList.ActualHeight > 0 ? PageList.ActualHeight : Math.Max(0, ActualHeight - 160);
        return new Size(w, h);
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
        var index = item.PageNumber - 1;
        if (!_rendering.Add(index)) return;

        var doc = _document;
        var token = _cts.Token;
        var dpi = 96.0 * item.Scale;
        var capturedScale = item.Scale;

        _ = Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using var page = doc.GetPage(index);
                var bmp = page.Render(dpi);
                bmp.Freeze();
                token.ThrowIfCancellationRequested();

                Dispatcher.Invoke(() =>
                {
                    // Drop stale renders if the user zoomed in the meantime.
                    if (Math.Abs(item.Scale - capturedScale) < 0.0001)
                    {
                        item.Image = bmp;
                    }
                    _rendering.Remove(index);
                });
            }
            catch
            {
                Dispatcher.Invoke(() => _rendering.Remove(index));
            }
        }, token);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0) return;
        UpdateCurrentPageFromScroll();
    }

    private void UpdateCurrentPageFromScroll()
    {
        if (_items is null || _items.Count == 0 || _scroller is null) return;
        var midY = _scroller.VerticalOffset + _scroller.ViewportHeight / 2.0;
        double accum = PageList.Padding.Top;
        for (int i = 0; i < _items.Count; i++)
        {
            var h = _items[i].DisplayHeight + 12; // ListBoxItem.Margin bottom
            if (midY < accum + h)
            {
                if (_currentPageIndex != i)
                {
                    _currentPageIndex = i;
                    UpdatePageIndicator();
                }
                return;
            }
            accum += h;
        }
        if (_currentPageIndex != _items.Count - 1)
        {
            _currentPageIndex = _items.Count - 1;
            UpdatePageIndicator();
        }
    }

    private void UpdatePageIndicator()
    {
        if (_items is null) return;
        PageIndicator.Text = $"{_currentPageIndex + 1} / {_items.Count}";
    }

    private void UpdateZoomIndicator() => ZoomIndicator.Text = $"{_scale * 100:0}%";

    private void OnPrevPageClick(object sender, RoutedEventArgs e) => GoToPage(_currentPageIndex - 1);
    private void OnNextPageClick(object sender, RoutedEventArgs e) => GoToPage(_currentPageIndex + 1);

    private void GoToPage(int index)
    {
        if (_items is null || _items.Count == 0) return;
        index = Math.Clamp(index, 0, _items.Count - 1);
        var item = _items[index];
        PageList.ScrollIntoView(item);
        _currentPageIndex = index;
        UpdatePageIndicator();
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => SetScale(_scale * ZoomStep);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => SetScale(_scale / ZoomStep);

    private void OnFitWindowClick(object sender, RoutedEventArgs e)
    {
        if (_items is null || _items.Count == 0) return;
        var f = _items[0];
        SetScale(ComputeFitScale(f.WidthPoints, f.HeightPoints));
    }

    private void SetScale(double newScale)
    {
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        if (Math.Abs(newScale - _scale) < 0.0001) return;
        _scale = newScale;
        UpdateZoomIndicator();
        if (_items is null) return;
        foreach (var it in _items)
        {
            it.Image = null;
            it.Scale = _scale;
        }
        // Trigger re-render of pages currently realized.
        for (int i = 0; i < _items.Count; i++)
        {
            if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is not null)
            {
                EnsureRendered(_items[i]);
            }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.PageDown:
            case Key.Right:
                OnNextPageClick(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.PageUp:
            case Key.Left:
                OnPrevPageClick(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Home:
                GoToPage(0);
                e.Handled = true;
                break;
            case Key.End:
                if (_items is not null) GoToPage(_items.Count - 1);
                e.Handled = true;
                break;
        }
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
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
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not print this PDF.\n\n{ex.Message}",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var deeper = FindVisualChild<T>(child);
            if (deeper is not null) return deeper;
        }
        return null;
    }
}
