using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace BarePDF;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand OpenCommand = new();
    public static readonly RoutedCommand CloseDocumentCommand = new();
    public static readonly RoutedCommand PrintCommand = new();

    private string? _currentPdfPath;

    public MainWindow()
    {
        InitializeComponent();

        CommandBindings.Add(new CommandBinding(OpenCommand, (_, _) => OnOpenClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(CloseDocumentCommand, (_, _) => OnCloseDocumentClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(PrintCommand, (_, _) => OnPrintClick(this, new RoutedEventArgs())));
    }

    public void OpenPdf(string path)
    {
        if (!File.Exists(path)) return;

        _currentPdfPath = path;
        EmptyState.Visibility = Visibility.Collapsed;
        Title = $"{Path.GetFileName(path)} — BarePDF";

        // TODO: hand `path` to the PDFium-backed viewer once the renderer is wired in.
    }

    public void CloseDocument()
    {
        _currentPdfPath = null;
        EmptyState.Visibility = Visibility.Visible;
        Title = "BarePDF";
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = "Open PDF"
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenPdf(dialog.FileName);
        }
    }

    private void OnCloseDocumentClick(object sender, RoutedEventArgs e) => CloseDocument();

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_currentPdfPath is null) return;
        // Wired once the PDFium renderer can supply pages to PrintDialog.
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        // Wired once settings + first-run mode selection lands.
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "BarePDF\n\nA fast, clean, distraction-free PDF viewer for Windows.\nNo ads. No cloud. No AI. Just PDFs.",
            "About BarePDF",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
