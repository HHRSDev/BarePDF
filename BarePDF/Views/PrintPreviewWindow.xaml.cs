using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using BarePDF.Pdfium;

namespace BarePDF.Views;

public partial class PrintPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    private const double PreviewDpi = 72.0;

    private readonly PdfDocument _document;

    public PrintPreviewWindow(PdfDocument document, Window? owner = null)
    {
        _document = document;
        if (owner is not null) Owner = owner;
        InitializeComponent();

        PageList.ItemsSource = RenderPages(document);
    }

    private static IReadOnlyList<BitmapSource> RenderPages(PdfDocument document)
    {
        var list = new List<BitmapSource>(document.PageCount);
        for (int i = 0; i < document.PageCount; i++)
        {
            using var page = document.GetPage(i);
            list.Add(page.Render(PreviewDpi));
        }
        return list;
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            var pageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            var paginator = new PdfPrintPaginator(_document, pageSize);
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
}
