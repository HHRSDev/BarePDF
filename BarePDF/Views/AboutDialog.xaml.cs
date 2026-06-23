using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace BarePDF.Views;

public partial class AboutDialog : Wpf.Ui.Controls.FluentWindow
{
    private const string RepoUrl = "https://github.com/HHRSDev/BarePDF";

    public AboutDialog(Window? owner = null)
    {
        if (owner is not null) Owner = owner;
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? "Version unknown"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnRepoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }
        catch { /* silent */ }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
