using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BarePDF.Pdfium;
using BarePDF.Settings;
using BarePDF.Views;
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

    public async Task OpenPdf(string path)
    {
        if (!File.Exists(path)) return;

        _currentPdfPath = path;
        EmptyState.Visibility = Visibility.Collapsed;
        Viewer.Visibility = Visibility.Visible;
        Title = $"{Path.GetFileName(path)} — BarePDF";

        try
        {
            await Viewer.OpenAsync(path);
        }
        catch (PdfException ex)
        {
            CloseDocument();
            MessageBox.Show(this,
                $"Could not open this PDF.\n\n{ex.Message}",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void CloseDocument()
    {
        Viewer.Close();
        Viewer.Visibility = Visibility.Collapsed;
        _currentPdfPath = null;
        EmptyState.Visibility = Visibility.Visible;
        Title = "BarePDF";
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = "Open PDF"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenPdf(dialog.FileName);
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
        var settings = SettingsStore.Load();
        var dialog = new InstanceModeDialog(isFirstRun: false, currentMode: settings.InstanceMode)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.SelectedMode is not { } chosen) return;
        if (chosen == settings.InstanceMode) return;

        settings.InstanceMode = chosen;
        SettingsStore.Save(settings);

        MessageBox.Show(
            this,
            "Instance mode updated. The change takes effect the next time BarePDF starts.",
            "BarePDF",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
