using System.Windows;
using PublisherRip.App.Services;

namespace PublisherRip.App;

public partial class InstallOptionsWindow : Window
{
    public InstallOptionsWindow(InstallOptions current, bool isFirstRun)
    {
        InitializeComponent();

        DesktopShortcutCheckBox.IsChecked = current.DesktopShortcut;
        StartMenuShortcutCheckBox.IsChecked = current.StartMenuShortcut;
        CommandLineLauncherCheckBox.IsChecked = current.CommandLineLauncher;

        if (isFirstRun)
        {
            CancelButton.Content = "Skip";
        }
        else
        {
            HeadingText.Text = "Shortcuts and launcher";
            IntroText.Text = "Choose how you'd like to launch Publisher RIP.";
        }
    }

    /// <summary>The user's choices, or null if they cancelled.</summary>
    public InstallOptions? Result { get; private set; }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        Result = new InstallOptions(
            DesktopShortcutCheckBox.IsChecked == true,
            StartMenuShortcutCheckBox.IsChecked == true,
            CommandLineLauncherCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
