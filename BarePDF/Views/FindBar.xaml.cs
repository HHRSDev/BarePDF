using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BarePDF.Views;

public partial class FindBar : UserControl
{
    public event Action<string>? QueryChanged;
    public event Action? NavigatePrev;
    public event Action? NavigateNext;
    public event Action? CloseRequested;
    public event Action? OptionsChanged;

    public FindBar()
    {
        InitializeComponent();
    }

    public string Query => QueryBox.Text;
    public bool CaseSensitive => CaseSensitiveCheck.IsChecked == true;
    public bool WholeWord => WholeWordCheck.IsChecked == true;

    public new void Focus()
    {
        QueryBox.Focus();
        QueryBox.SelectAll();
    }

    public void SetMatchInfo(int currentIndex, int totalCount)
    {
        if (string.IsNullOrEmpty(QueryBox.Text))
        {
            CountLabel.Text = string.Empty;
        }
        else if (totalCount == 0)
        {
            CountLabel.Text = "No matches";
        }
        else
        {
            CountLabel.Text = $"{currentIndex + 1} of {totalCount}";
        }
    }

    private void OnQueryTextChanged(object sender, TextChangedEventArgs e) =>
        QueryChanged?.Invoke(QueryBox.Text);

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) NavigatePrev?.Invoke();
            else NavigateNext?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => NavigatePrev?.Invoke();
    private void OnNextClick(object sender, RoutedEventArgs e) => NavigateNext?.Invoke();
    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private void OnOptionsChanged(object sender, RoutedEventArgs e) => OptionsChanged?.Invoke();
}
