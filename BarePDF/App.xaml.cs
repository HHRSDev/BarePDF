using System.IO;
using System.Windows;

namespace BarePDF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        var path = ResolvePdfPath(e.Args);
        if (path is not null)
        {
            window.OpenPdf(path);
        }
        window.Show();
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
