using System.Windows;
using System.Windows.Input;

namespace BarePDF.Views;

public partial class PasswordPromptDialog : Wpf.Ui.Controls.FluentWindow
{
    public string Password => PasswordInput.Password;

    public PasswordPromptDialog(string fileName, bool retry)
    {
        InitializeComponent();

        Heading.Text = retry ? "Incorrect password" : "Password required";
        Subheading.Text = retry
            ? $"The password you entered for \"{fileName}\" is incorrect."
            : $"\"{fileName}\" is password-protected.";

        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }
}
