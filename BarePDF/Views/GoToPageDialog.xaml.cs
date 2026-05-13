using System.Windows;
using System.Windows.Input;

namespace BarePDF.Views;

public partial class GoToPageDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly int _pageCount;

    public int? SelectedPageNumber { get; private set; }

    public GoToPageDialog(int pageCount)
    {
        _pageCount = pageCount;
        InitializeComponent();
        Prompt.Text = $"Go to page (1–{pageCount}):";
        Loaded += (_, _) =>
        {
            PageInput.Focus();
            PageInput.SelectAll();
        };
    }

    private void OnPageInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOkClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PageInput.Text, out var n) && n >= 1 && n <= _pageCount)
        {
            SelectedPageNumber = n;
            DialogResult = true;
        }
        else
        {
            System.Media.SystemSounds.Beep.Play();
            PageInput.Focus();
            PageInput.SelectAll();
        }
    }
}
