using System.Windows;
using BarePDF.Pdfium;

namespace BarePDF.Views;

public partial class PrintPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly PdfDocument _document;

    public PrintPreviewWindow(PdfDocument document, Window? owner = null)
    {
        _document = document;
        if (owner is not null) Owner = owner;
        InitializeComponent();

        var pageSize = new Size(8.5 * 96.0, 11.0 * 96.0);
        DocViewer.Document = new PdfPrintPaginator(document, pageSize, renderDpi: 96.0);
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
