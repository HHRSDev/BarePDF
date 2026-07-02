using System.Windows;

namespace BarePDF.Views;

public partial class ErrorDetailsDialog : Wpf.Ui.Controls.FluentWindow
{
    public ErrorDetailsDialog(string title, string headline, string details, Window? owner = null)
    {
        if (owner is not null) Owner = owner;
        InitializeComponent();

        Title = title;
        HeadlineText.Text = headline;
        DetailsBox.Text = details;
    }

    public static void Show(Window? owner, string title, string headline, Exception ex)
    {
        var details = BuildExceptionText(ex);
        var dlg = new ErrorDetailsDialog(title, headline, details, owner);
        dlg.ShowDialog();
    }

    private static string BuildExceptionText(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var e = ex;
        var depth = 0;
        while (e is not null)
        {
            if (depth > 0) sb.AppendLine().AppendLine("--- Inner Exception ---");
            sb.Append(e.GetType().FullName).Append(": ").AppendLine(e.Message);
            if (e is System.Runtime.InteropServices.COMException com)
            {
                sb.Append("HRESULT: 0x").AppendLine(com.HResult.ToString("X8"));
            }
            if (e.TargetSite is not null)
            {
                sb.Append("Thrown by: ")
                  .Append(e.TargetSite.DeclaringType?.FullName ?? "?")
                  .Append('.')
                  .AppendLine(e.TargetSite.Name);
            }
            if (e.StackTrace is not null)
            {
                sb.AppendLine(e.StackTrace);
            }
            e = e.InnerException;
            depth++;
        }
        return sb.ToString();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsBox.Text);
        }
        catch { /* clipboard can fail under remote sessions; ignore */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
