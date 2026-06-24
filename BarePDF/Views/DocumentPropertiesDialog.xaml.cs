using System.IO;
using System.Windows;
using BarePDF.Pdfium;

namespace BarePDF.Views;

public partial class DocumentPropertiesDialog : Wpf.Ui.Controls.FluentWindow
{
    private const string NotSpecified = "—";

    public DocumentPropertiesDialog(PdfDocument document, string? filePath, Window? owner = null)
    {
        if (owner is not null) Owner = owner;
        InitializeComponent();
        Populate(document, filePath);
    }

    private void Populate(PdfDocument document, string? filePath)
    {
        DateTimeOffset? fileCreated = null;
        DateTimeOffset? fileModified = null;

        if (!string.IsNullOrEmpty(filePath))
        {
            FileNameText.Text = Path.GetFileName(filePath);
            FolderText.Text = Path.GetDirectoryName(filePath) ?? NotSpecified;
            FileNameText.ToolTip = filePath;
            FolderText.ToolTip = FolderText.Text;

            try
            {
                if (File.Exists(filePath))
                {
                    fileCreated = new DateTimeOffset(File.GetCreationTime(filePath));
                    fileModified = new DateTimeOffset(File.GetLastWriteTime(filePath));
                }
            }
            catch { /* ignore — fallback is a nicety, not a requirement */ }
        }
        else
        {
            FileNameText.Text = NotSpecified;
            FolderText.Text = NotSpecified;
        }

        PageCountText.Text = document.PageCount.ToString();
        FileSizeText.Text = FormatBytes(document.FileSize);

        var meta = document.GetMetadata();
        TitleText.Text = meta.Title ?? NotSpecified;
        AuthorText.Text = meta.Author ?? NotSpecified;
        SubjectText.Text = meta.Subject ?? NotSpecified;
        KeywordsText.Text = meta.Keywords ?? NotSpecified;
        CreatorText.Text = meta.Creator ?? NotSpecified;
        ProducerText.Text = meta.Producer ?? NotSpecified;
        CreatedText.Text = FormatDateWithFallback(meta.CreationDate, fileCreated);
        ModifiedText.Text = FormatDateWithFallback(meta.ModificationDate, fileModified);
        PdfVersionText.Text = meta.PdfVersion;
    }

    private static string FormatDateWithFallback(DateTimeOffset? pdfDate, DateTimeOffset? fileDate)
    {
        if (pdfDate is not null) return FormatDate(pdfDate);
        if (fileDate is not null) return $"{FormatDate(fileDate)} (file)";
        return NotSpecified;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return NotSpecified;
        string[] units = ["bytes", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        var human = unit == 0 ? $"{bytes:N0} {units[0]}" : $"{size:0.##} {units[unit]} ({bytes:N0} bytes)";
        return human;
    }

    private static string FormatDate(DateTimeOffset? dt)
    {
        return dt is null ? NotSpecified : dt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
