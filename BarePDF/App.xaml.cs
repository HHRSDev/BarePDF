using System.IO;
using System.Windows;
using BarePDF.Settings;
using BarePDF.Views;

namespace BarePDF;

public partial class App : Application
{
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

        var window = new MainWindow();
        var path = ResolvePdfPath(e.Args);
        if (path is not null)
        {
            window.OpenPdf(path);
        }
        MainWindow = window;
        window.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
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
