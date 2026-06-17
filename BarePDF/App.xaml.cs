using System.IO;
using System.Windows;
using BarePDF.Instances;
using BarePDF.Settings;
using BarePDF.Views;
using Wpf.Ui.Appearance;

namespace BarePDF;

public partial class App : Application
{
    private InstanceCoordinator? _coordinator;
    private static Window? _watchedWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settings = SettingsStore.Load();
        ApplyThemeWithoutWatcher(settings.Theme ?? AppTheme.System);

        // Print-preview shell verb: launch straight into PrintPreviewWindow as the
        // only top-level window. Bypasses instance coordination so an already-open
        // BarePDF viewer is not disturbed.
        if (TryGetPrintPreviewPath(e.Args, out var previewPath))
        {
            StartPrintPreviewOnlyMode(previewPath);
            return;
        }

        if (settings.InstanceMode is null)
        {
            var dialog = new InstanceModeDialog(isFirstRun: true, currentMode: null);
            if (dialog.ShowDialog() != true || dialog.SelectedMode is null)
            {
                Shutdown();
                return;
            }
            settings.InstanceMode = dialog.SelectedMode;
            SettingsStore.Save(settings);
        }

        var mode = settings.InstanceMode!.Value;
        var path = ResolvePdfPath(e.Args);

        if (mode is InstanceMode.Singleton or InstanceMode.Tabbed)
        {
            _coordinator = new InstanceCoordinator();
            if (!_coordinator.TryAcquirePrimary())
            {
                _coordinator.SendToPrimary(path ?? string.Empty, TimeSpan.FromSeconds(2));
                _coordinator.Dispose();
                _coordinator = null;
                Shutdown();
                return;
            }
        }

        var window = new MainWindow(mode);
        MainWindow = window;
        window.Show();

        if ((settings.Theme ?? AppTheme.System) == AppTheme.System)
        {
            SystemThemeWatcher.Watch(window);
            _watchedWindow = window;
        }

        if (path is not null)
        {
            _ = window.OpenPdf(path);
        }

        if (_coordinator?.IsPrimary == true)
        {
            _coordinator.PdfPathReceived += incoming =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Activate();
                    if (!string.IsNullOrEmpty(incoming))
                    {
                        _ = window.OpenPdf(incoming);
                    }
                });
            };
            _coordinator.StartListening();
        }

        if (settings.AutoCheckForUpdates ?? true)
        {
            _ = RunStartupUpdateCheck(window);
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private static async Task RunStartupUpdateCheck(MainWindow window)
    {
        var info = await Updates.UpdateChecker.CheckAsync();
        if (info is null) return;
        await window.Dispatcher.InvokeAsync(() => window.ShowUpdateNotice(info.Value));
    }

    internal static void ApplyTheme(AppTheme mode, Window window)
    {
        if (mode == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme();
            if (_watchedWindow is null)
            {
                SystemThemeWatcher.Watch(window);
                _watchedWindow = window;
            }
        }
        else
        {
            if (_watchedWindow is not null)
            {
                SystemThemeWatcher.UnWatch(_watchedWindow);
                _watchedWindow = null;
            }
            ApplicationThemeManager.Apply(
                mode == AppTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        }
    }

    private static void ApplyThemeWithoutWatcher(AppTheme mode)
    {
        if (mode == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme();
        }
        else
        {
            ApplicationThemeManager.Apply(
                mode == AppTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        _coordinator = null;
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { /* nothing we can do if even MessageBox fails */ }
        e.Handled = true; // keep the app alive so the user can see the message and continue
    }

    private static string? ResolvePdfPath(string[] args)
    {
        foreach (var arg in args)
        {
            if (!string.IsNullOrWhiteSpace(arg) && File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }
        }
        return null;
    }

    private static bool TryGetPrintPreviewPath(string[] args, out string path)
    {
        var requested = false;
        string? found = null;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "/printpreview", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-printpreview", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--printpreview", StringComparison.OrdinalIgnoreCase))
            {
                requested = true;
            }
            else if (!string.IsNullOrWhiteSpace(arg) && File.Exists(arg))
            {
                found = Path.GetFullPath(arg);
            }
        }
        if (requested && found is not null)
        {
            path = found;
            return true;
        }
        path = string.Empty;
        return false;
    }

    private void StartPrintPreviewOnlyMode(string path)
    {
        try
        {
            Pdfium.PdfDocument? document = null;
            string? attemptedPassword = null;
            var hasAttempted = false;
            while (document is null)
            {
                try
                {
                    document = Pdfium.PdfDocument.Open(path, attemptedPassword);
                }
                catch (Pdfium.PdfException ex) when (ex.ErrorCode == 4)
                {
                    var prompt = new PasswordPromptDialog(Path.GetFileName(path), retry: hasAttempted);
                    if (prompt.ShowDialog() != true)
                    {
                        Shutdown();
                        return;
                    }
                    attemptedPassword = prompt.Password;
                    hasAttempted = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Could not open this PDF.\n\n{ex.Message}",
                        "BarePDF",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                    return;
                }
            }

            var preview = new PrintPreviewWindow(document);
            preview.Title = $"Print Preview — {Path.GetFileName(path)}";
            preview.Closed += (_, _) =>
            {
                document.Dispose();
                Shutdown();
            };
            MainWindow = preview;
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            preview.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Print Preview failed to start.\n\n{ex.GetType().Name}: {ex.Message}",
                "BarePDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }
}
