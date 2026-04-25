using System.IO;
using System.Windows;
using BarePDF.Instances;
using BarePDF.Settings;
using BarePDF.Views;

namespace BarePDF;

public partial class App : Application
{
    private InstanceCoordinator? _coordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = SettingsStore.Load();
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

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        _coordinator = null;
        base.OnExit(e);
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
}
