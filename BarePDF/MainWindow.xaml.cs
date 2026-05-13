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
    public static readonly RoutedCommand FitPageCommand = new();
    public static readonly RoutedCommand FitPageHeightCommand = new();
    public static readonly RoutedCommand FitWidthCommand = new();
    public static readonly RoutedCommand ActualSizeCommand = new();
    public static readonly RoutedCommand ZoomInCommand = new();
    public static readonly RoutedCommand ZoomOutCommand = new();
    public static readonly RoutedCommand FindCommand = new();
    public static readonly RoutedCommand ToggleThumbnailsCommand = new();
    public static readonly RoutedCommand PageUpCommand = new();
    public static readonly RoutedCommand PageDownCommand = new();
    public static readonly RoutedCommand GoToFirstPageCommand = new();
    public static readonly RoutedCommand GoToLastPageCommand = new();
    public static readonly RoutedCommand GoToPageCommand = new();
    public static readonly RoutedCommand RotateRightCommand = new();
    public static readonly RoutedCommand RotateLeftCommand = new();
    public static readonly RoutedCommand ToggleSinglePageCommand = new();

    private readonly InstanceMode _mode;
    private string? _currentDocumentPath;

    public MainWindow() : this(InstanceMode.Singleton) { }

    public MainWindow(InstanceMode mode)
    {
        _mode = mode;
        InitializeComponent();

        CommandBindings.Add(new CommandBinding(OpenCommand, (_, _) => OnOpenClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(CloseDocumentCommand, (_, _) => OnCloseDocumentClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(PrintCommand, (_, _) => OnPrintClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(FitPageCommand, (_, _) => SetActiveZoomMode(ZoomMode.FitPage)));
        CommandBindings.Add(new CommandBinding(FitPageHeightCommand, (_, _) => SetActiveZoomMode(ZoomMode.FitPageHeight)));
        CommandBindings.Add(new CommandBinding(FitWidthCommand, (_, _) => SetActiveZoomMode(ZoomMode.FitWidth)));
        CommandBindings.Add(new CommandBinding(ActualSizeCommand, (_, _) => SetActiveZoomMode(ZoomMode.ActualSize)));
        CommandBindings.Add(new CommandBinding(ZoomInCommand, (_, _) => GetActiveViewer()?.ZoomIn()));
        CommandBindings.Add(new CommandBinding(ZoomOutCommand, (_, _) => GetActiveViewer()?.ZoomOut()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            (_, _) => GetActiveViewer()?.CopySelectedText(),
            (_, e) => e.CanExecute = GetActiveViewer()?.HasSelection ?? false));
        CommandBindings.Add(new CommandBinding(FindCommand, (_, _) => GetActiveViewer()?.ShowFindBar()));
        CommandBindings.Add(new CommandBinding(ToggleThumbnailsCommand, (_, _) => GetActiveViewer()?.ToggleThumbnails()));
        CommandBindings.Add(new CommandBinding(PageUpCommand, (_, _) => GetActiveViewer()?.ScrollPageUp()));
        CommandBindings.Add(new CommandBinding(PageDownCommand, (_, _) => GetActiveViewer()?.ScrollPageDown()));
        CommandBindings.Add(new CommandBinding(GoToFirstPageCommand, (_, _) => GetActiveViewer()?.ScrollToFirstPage()));
        CommandBindings.Add(new CommandBinding(GoToLastPageCommand, (_, _) => GetActiveViewer()?.ScrollToLastPage()));
        CommandBindings.Add(new CommandBinding(GoToPageCommand, (_, _) => OpenGoToPageDialog()));
        CommandBindings.Add(new CommandBinding(RotateRightCommand, (_, _) => GetActiveViewer()?.RotateRight()));
        CommandBindings.Add(new CommandBinding(RotateLeftCommand, (_, _) => GetActiveViewer()?.RotateLeft()));
        CommandBindings.Add(new CommandBinding(ToggleSinglePageCommand, (_, _) => ToggleSinglePage()));

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
        _currentDocumentPath = path;
        ApplyTitle();

        try
        {
            await Viewer.OpenAsync(path);
            AddToRecents(path);
            ApplyAutoFitWindowWidth(Viewer);
            SyncDisplayModeMenu();
        }
        catch (OperationCanceledException)
        {
            CloseDocument();
        }
        catch (PdfException ex)
        {
            CloseDocument();
            ShowOpenError(ex);
        }
    }

    private void ApplyAutoFitWindowWidth(PdfViewer viewer)
    {
        if (_mode == InstanceMode.Tabbed) return;
        if (WindowState != WindowState.Normal) return;
        var settings = SettingsStore.Load();
        if (settings.AutoFitWindowWidth != true) return;

        var pageWidth = viewer.FirstPageDisplayWidth;
        if (pageWidth <= 0) return;

        const double chrome = 80;
        var maxWidth = SystemParameters.WorkArea.Width;
        Width = Math.Min(pageWidth + chrome, maxWidth);
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
            AddToRecents(path);
            SyncDisplayModeMenu();
        }
        catch (OperationCanceledException)
        {
            RemoveTabQuietly(viewer, tab);
        }
        catch (PdfException ex)
        {
            RemoveTabQuietly(viewer, tab);
            ShowOpenError(ex);
        }
    }

    private void RemoveTabQuietly(PdfViewer viewer, TabItem tab)
    {
        viewer.Close();
        TabHost.Items.Remove(tab);
        if (TabHost.Items.Count == 0)
        {
            TabHost.Visibility = Visibility.Collapsed;
            ShowEmptyState();
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
        _currentDocumentPath = null;
        ApplyTitle();
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

    private void OnPrintPreviewClick(object sender, RoutedEventArgs e)
    {
        var viewer = GetActiveViewer();
        if (viewer is null || !viewer.HasDocument) return;
        viewer.ShowPrintPreview(this);
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => GetActiveViewer()?.ShowFindBar();
    private void OnGoToPageClick(object sender, RoutedEventArgs e) => OpenGoToPageDialog();
    private void OnRotateRightClick(object sender, RoutedEventArgs e) => GetActiveViewer()?.RotateRight();
    private void OnRotateLeftClick(object sender, RoutedEventArgs e) => GetActiveViewer()?.RotateLeft();
    private void OnSinglePageClick(object sender, RoutedEventArgs e) => ToggleSinglePage();

    private void ToggleSinglePage()
    {
        var viewer = GetActiveViewer();
        if (viewer is null) return;
        var newMode = viewer.DisplayMode == PageDisplayMode.SinglePage
            ? PageDisplayMode.Continuous
            : PageDisplayMode.SinglePage;
        viewer.SetDisplayMode(newMode);
        SyncDisplayModeMenu();
    }

    private void SyncDisplayModeMenu()
    {
        var viewer = GetActiveViewer();
        SinglePageMenuItem.IsChecked = viewer?.DisplayMode == PageDisplayMode.SinglePage;
    }

    private void OpenGoToPageDialog()
    {
        var viewer = GetActiveViewer();
        if (viewer is null || !viewer.HasDocument || viewer.PageCount == 0) return;

        var dialog = new GoToPageDialog(viewer.PageCount) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedPageNumber is { } n)
        {
            viewer.GoToPage(n - 1);
        }
    }
    private void OnToggleThumbnailsClick(object sender, RoutedEventArgs e) => GetActiveViewer()?.ToggleThumbnails();

    private void OnFitPageClick(object sender, RoutedEventArgs e) => SetActiveZoomMode(ZoomMode.FitPage);
    private void OnFitPageHeightClick(object sender, RoutedEventArgs e) => SetActiveZoomMode(ZoomMode.FitPageHeight);
    private void OnFitWidthClick(object sender, RoutedEventArgs e) => SetActiveZoomMode(ZoomMode.FitWidth);
    private void OnActualSizeClick(object sender, RoutedEventArgs e) => SetActiveZoomMode(ZoomMode.ActualSize);
    private void OnZoomInClick(object sender, RoutedEventArgs e) => GetActiveViewer()?.ZoomIn();
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => GetActiveViewer()?.ZoomOut();

    private void SetActiveZoomMode(ZoomMode mode)
    {
        var viewer = GetActiveViewer();
        if (viewer is null || !viewer.HasDocument) return;
        viewer.SetZoomMode(mode);
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
            currentTheme: settings.Theme,
            currentAutoFitWidth: settings.AutoFitWindowWidth ?? false,
            currentTitleBarMode: settings.TitleBarFilenameMode ?? TitleBarFilenameMode.Filename,
            currentAutoCheckForUpdates: settings.AutoCheckForUpdates ?? true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.SelectedMode is not { } chosenMode) return;

        var modeChanged = chosenMode != settings.InstanceMode;
        var themeChanged = dialog.SelectedTheme != (settings.Theme ?? AppTheme.System);
        var autoFitChanged = dialog.AutoFitWindowWidth != (settings.AutoFitWindowWidth ?? false);
        var titleBarChanged = dialog.SelectedTitleBarMode != (settings.TitleBarFilenameMode ?? TitleBarFilenameMode.Filename);
        var autoCheckChanged = dialog.AutoCheckForUpdates != (settings.AutoCheckForUpdates ?? true);
        if (!modeChanged && !themeChanged && !autoFitChanged && !titleBarChanged && !autoCheckChanged) return;

        settings.InstanceMode = chosenMode;
        settings.Theme = dialog.SelectedTheme;
        settings.AutoFitWindowWidth = dialog.AutoFitWindowWidth;
        settings.TitleBarFilenameMode = dialog.SelectedTitleBarMode;
        settings.AutoCheckForUpdates = dialog.AutoCheckForUpdates;
        SettingsStore.Save(settings);

        if (themeChanged)
        {
            App.ApplyTheme(dialog.SelectedTheme, this);
        }

        if (titleBarChanged)
        {
            ApplyTitle();
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

    private const int MaxRecentFiles = 10;

    private void OnFileMenuOpened(object sender, RoutedEventArgs e)
    {
        BuildRecentMenu();
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var settings = SettingsStore.Load();
        var recents = settings.RecentFiles ?? new System.Collections.Generic.List<string>();

        if (recents.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }

        for (int i = 0; i < recents.Count; i++)
        {
            var p = recents[i];
            var name = Path.GetFileName(p);
            var item = new MenuItem
            {
                Header = $"_{(i + 1) % 10} {name}",
                ToolTip = p,
                Tag = p,
            };
            item.Click += OnRecentItemClick;
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear Recent" };
        clear.Click += OnClearRecentClick;
        RecentMenu.Items.Add(clear);
    }

    private async void OnRecentItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string path) return;
        if (!File.Exists(path))
        {
            RemoveFromRecents(path);
            MessageBox.Show(this,
                $"\"{Path.GetFileName(path)}\" could not be found.\nIt may have been moved or deleted.",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        await OpenPdf(path);
    }

    private void OnClearRecentClick(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStore.Load();
        settings.RecentFiles = new System.Collections.Generic.List<string>();
        SettingsStore.Save(settings);
    }

    private void ApplyTitle()
    {
        var settings = SettingsStore.Load();
        var mode = settings.TitleBarFilenameMode ?? TitleBarFilenameMode.Filename;
        if (_currentDocumentPath is null || mode == TitleBarFilenameMode.Off)
        {
            Title = "BarePDF";
            return;
        }
        var label = mode == TitleBarFilenameMode.FullPath
            ? _currentDocumentPath
            : Path.GetFileName(_currentDocumentPath);
        Title = $"{label} — BarePDF";
    }

    private static void AddToRecents(string path)
    {
        var settings = SettingsStore.Load();
        var list = settings.RecentFiles ?? new System.Collections.Generic.List<string>();
        var full = Path.GetFullPath(path);
        list.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, full);
        while (list.Count > MaxRecentFiles) list.RemoveAt(list.Count - 1);
        settings.RecentFiles = list;
        SettingsStore.Save(settings);
    }

    private static void RemoveFromRecents(string path)
    {
        var settings = SettingsStore.Load();
        var list = settings.RecentFiles ?? new System.Collections.Generic.List<string>();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        settings.RecentFiles = list;
        SettingsStore.Save(settings);
    }

    private Updates.UpdateInfo? _pendingUpdate;

    public void ShowUpdateNotice(Updates.UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateNoticeText.Text = $"BarePDF {info.Tag} is available.";
        UpdateNoticeBar.Visibility = Visibility.Visible;
    }

    private void OnDismissUpdateNoticeClick(object sender, RoutedEventArgs e)
    {
        UpdateNoticeBar.Visibility = Visibility.Collapsed;
    }

    private void OnViewUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is { } info && !string.IsNullOrWhiteSpace(info.ReleaseUrl))
        {
            OpenInBrowser(info.ReleaseUrl);
        }
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        var info = await Updates.UpdateChecker.CheckAsync();
        if (info is { } update)
        {
            _pendingUpdate = update;
            var result = MessageBox.Show(this,
                $"BarePDF {update.Tag} is available.\n\nOpen the release page in your browser?",
                "BarePDF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                OpenInBrowser(update.ReleaseUrl);
            }
            UpdateNoticeText.Text = $"BarePDF {update.Tag} is available.";
            UpdateNoticeBar.Visibility = Visibility.Visible;
        }
        else
        {
            MessageBox.Show(this,
                "You're running the latest version of BarePDF.",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* user-friendly silent fail */ }
    }

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
        if (TabHost.SelectedItem is TabItem t)
        {
            _currentDocumentPath = t.ToolTip as string;
        }
        else
        {
            _currentDocumentPath = null;
        }
        ApplyTitle();
        SyncDisplayModeMenu();
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
