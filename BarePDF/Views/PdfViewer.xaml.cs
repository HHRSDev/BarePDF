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

        var items = new ObservableCollection<PdfPageItem>();
        for (int i = 0; i < sizes.Length; i++)
        {
            items.Add(new PdfPageItem(pageNumber: i + 1, sizes[i].w, sizes[i].h));
        }
        PageList.ItemsSource = items;

        _renderCts = new CancellationTokenSource();
        var token = _renderCts.Token;
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                using var page = document.GetPage(i);
                var image = page.Render(96.0);
                var index = i;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    items[index].Image = image;
                });
            }
        }, token);
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

        PageList.ItemsSource = null;

        _document?.Dispose();
        _document = null;
    }
}
