using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BarePDF.Pdfium;
using BarePDF.Settings;
using BarePDF.Views;
using Microsoft.Win32;

namespace BarePDF;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public static readonly RoutedCommand OpenCommand = new();
    public static readonly RoutedCommand CloseDocumentCommand = new();
    public static readonly RoutedCommand PrintCommand = new();

    private readonly InstanceMode _mode;

    public MainWindow() : this(InstanceMode.Singleton) { }

    public MainWindow(InstanceMode mode)
    {
        _mode = mode;
        InitializeComponent();

        CommandBindings.Add(new CommandBinding(OpenCommand, (_, _) => OnOpenClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(CloseDocumentCommand, (_, _) => OnCloseDocumentClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(PrintCommand, (_, _) => OnPrintClick(this, new RoutedEventArgs())));

        Closed += OnWindowClosed;
    }

    public Task OpenPdf(string path)
    {
        if (!File.Exists(path)) return Task.CompletedTask;
        return _mode switch
        {
            InstanceMode.Tabbed => AddTab(path),
            InstanceMode.Multiple when Viewer.HasDocument => OpenInNewWindow(path),
            _ => LoadInSingleViewer(path),
        };
    }

    private Task OpenInNewWindow(string path)
    {
        var window = new MainWindow(_mode);
        window.Show();
        return window.OpenPdf(path);
    }

    public void CloseDocument()
    {
        if (_mode == InstanceMode.Tabbed)
        {
            if (TabHost.SelectedItem is TabItem t) CloseTab(t);
        }
        else
        {
            Viewer.Close();
            Viewer.Visibility = Visibility.Collapsed;
            ShowEmptyState();
        }
    }

    private async Task LoadInSingleViewer(string path)
    {
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
            ShowOpenError(ex);
        }
    }

    private async Task AddTab(string path)
    {
        var viewer = new PdfViewer();
        var tab = new TabItem
        {
            Header = Path.GetFileName(path),
            Content = viewer,
            ToolTip = path,
        };

        EmptyState.Visibility = Visibility.Collapsed;
        TabHost.Items.Add(tab);
        TabHost.SelectedItem = tab;
        TabHost.Visibility = Visibility.Visible;

        try
        {
            await viewer.OpenAsync(path);
        }
        catch (PdfException ex)
        {
            viewer.Close();
            TabHost.Items.Remove(tab);
            if (TabHost.Items.Count == 0)
            {
                TabHost.Visibility = Visibility.Collapsed;
                ShowEmptyState();
            }
            ShowOpenError(ex);
        }
    }

    private void CloseTab(TabItem tab)
    {
        if (tab.Content is PdfViewer v) v.Close();
        TabHost.Items.Remove(tab);
        if (TabHost.Items.Count == 0)
        {
            TabHost.Visibility = Visibility.Collapsed;
            ShowEmptyState();
        }
    }

    private void ShowEmptyState()
    {
        EmptyState.Visibility = Visibility.Visible;
        Title = "BarePDF";
    }

    private void ShowOpenError(PdfException ex)
    {
        MessageBox.Show(this,
            $"Could not open this PDF.\n\n{ex.Message}",
            "BarePDF",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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
        var viewer = GetActiveViewer();
        if (viewer is null || !viewer.HasDocument) return;
        viewer.Print(this);
    }

    private PdfViewer? GetActiveViewer()
    {
        if (_mode == InstanceMode.Tabbed)
        {
            return TabHost.SelectedItem is TabItem t ? t.Content as PdfViewer : null;
        }
        return Viewer;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStore.Load();
        var dialog = new InstanceModeDialog(
            isFirstRun: false,
            currentMode: settings.InstanceMode,
            currentTheme: settings.Theme)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.SelectedMode is not { } chosenMode) return;

        var modeChanged = chosenMode != settings.InstanceMode;
        var themeChanged = dialog.SelectedTheme != (settings.Theme ?? AppTheme.System);
        if (!modeChanged && !themeChanged) return;

        settings.InstanceMode = chosenMode;
        settings.Theme = dialog.SelectedTheme;
        SettingsStore.Save(settings);

        if (themeChanged)
        {
            App.ApplyTheme(dialog.SelectedTheme, this);
        }

        if (modeChanged)
        {
            MessageBox.Show(
                this,
                "Instance mode updated. The change takes effect the next time BarePDF starts.",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, TabHost)) return;
        if (TabHost.SelectedItem is TabItem t && t.Header is string title)
        {
            Title = $"{title} — BarePDF";
        }
        else if (TabHost.Items.Count == 0)
        {
            Title = "BarePDF";
        }
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TabItem tab })
        {
            CloseTab(tab);
        }
        e.Handled = true;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_mode == InstanceMode.Tabbed)
        {
            foreach (var item in TabHost.Items.OfType<TabItem>())
            {
                (item.Content as PdfViewer)?.Close();
            }
        }
        else
        {
            Viewer.Close();
        }
    }
}
