using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BarePDF.Pdfium;

namespace BarePDF.Views;

public partial class PdfViewer : UserControl
{
    private PdfDocument? _document;
    private CancellationTokenSource? _renderCts;
    private readonly HashSet<int> _renderingPages = new();

    public PdfViewer()
    {
        InitializeComponent();
    }

    public bool HasDocument => _document is not null;

    public async Task OpenAsync(string path)
    {
        Close();

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
        var items = new ObservableCollection<PdfPageItem>();
        for (int i = 0; i < sizes.Length; i++)
        {
            items.Add(new PdfPageItem(pageNumber: i + 1, sizes[i].w, sizes[i].h));
        }
        PageList.ItemsSource = items;
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

        PageList.ItemsSource = null;

        _document?.Dispose();
        _document = null;
    }

    private void OnPageContainerLoaded(object sender, RoutedEventArgs e)
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

        _ = Task.Run(() =>
        {
            try
            {
                if (token.IsCancellationRequested) return;
                using var page = doc.GetPage(index);
                if (token.IsCancellationRequested) return;
                var bitmap = page.Render(96.0);
                Dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        item.Image = bitmap;
                    }
                    _renderingPages.Remove(index);
                });
            }
            catch
            {
                Dispatcher.InvokeAsync(() => _renderingPages.Remove(index));
            }
        }, token);
    }
}
