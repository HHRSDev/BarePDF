using System.Windows;
using BarePDF.Settings;

namespace BarePDF.Views;

public partial class InstanceModeDialog : Wpf.Ui.Controls.FluentWindow
{
    public InstanceMode? SelectedMode { get; private set; }

    public AppTheme SelectedTheme => ThemeCombo.SelectedIndex switch
    {
        1 => AppTheme.Light,
        2 => AppTheme.Dark,
        _ => AppTheme.System,
    };

    public bool AutoFitWindowWidth => AutoFitWidthCheckbox.IsChecked == true;

    public TitleBarFilenameMode SelectedTitleBarMode => TitleBarModeCombo.SelectedIndex switch
    {
        0 => TitleBarFilenameMode.Off,
        2 => TitleBarFilenameMode.FullPath,
        _ => TitleBarFilenameMode.Filename,
    };

    public InstanceModeDialog(
        bool isFirstRun,
        InstanceMode? currentMode,
        AppTheme? currentTheme = null,
        bool currentAutoFitWidth = false,
        TitleBarFilenameMode currentTitleBarMode = TitleBarFilenameMode.Filename)
    {
        InitializeComponent();

        Heading.Text = isFirstRun ? "Welcome to BarePDF" : "Choose how BarePDF opens PDFs";
        Title = isFirstRun ? "Welcome to BarePDF" : "BarePDF Settings";

        switch (currentMode)
        {
            case InstanceMode.Singleton: SingletonOption.IsChecked = true; break;
            case InstanceMode.Multiple: MultipleOption.IsChecked = true; break;
            case InstanceMode.Tabbed: TabbedOption.IsChecked = true; break;
        }

        var showExtras = isFirstRun ? Visibility.Collapsed : Visibility.Visible;
        AppearanceSection.Visibility = showExtras;
        WindowSection.Visibility = showExtras;
        ThemeCombo.SelectedIndex = (currentTheme ?? AppTheme.System) switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0,
        };
        AutoFitWidthCheckbox.IsChecked = currentAutoFitWidth;
        TitleBarModeCombo.SelectedIndex = currentTitleBarMode switch
        {
            TitleBarFilenameMode.Off => 0,
            TitleBarFilenameMode.FullPath => 2,
            _ => 1,
        };

        UpdateOkEnabled();
        SingletonOption.Checked += (_, _) => UpdateOkEnabled();
        MultipleOption.Checked += (_, _) => UpdateOkEnabled();
        TabbedOption.Checked += (_, _) => UpdateOkEnabled();
    }

    private void UpdateOkEnabled()
    {
        OkButton.IsEnabled = SingletonOption.IsChecked == true
                          || MultipleOption.IsChecked == true
                          || TabbedOption.IsChecked == true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (SingletonOption.IsChecked == true) SelectedMode = InstanceMode.Singleton;
        else if (MultipleOption.IsChecked == true) SelectedMode = InstanceMode.Multiple;
        else if (TabbedOption.IsChecked == true) SelectedMode = InstanceMode.Tabbed;
        DialogResult = true;
    }
}
