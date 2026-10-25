using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        ConfigPath.Text = App.State.ConfigPath;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = $"{AppIdentity.DisplayName} (WinUI) {version}";
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e) =>
        ConfigFileOpener.Open(App.State.ConfigPath, App.State.LogMessage);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{App.State.ConfigPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.State.LogMessage($"Failed to open the folder: {ex.Message}");
        }
    }
}
