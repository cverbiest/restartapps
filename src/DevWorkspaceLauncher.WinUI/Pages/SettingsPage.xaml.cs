using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    /// <summary>The SSH client choices, in the order of the combo box; null is "automatic".</summary>
    private static readonly SshClient?[] SshClients = [null, DevWorkspaceLauncher.SshClient.WezTerm, DevWorkspaceLauncher.SshClient.WindowsTerminal];

    public SettingsPage()
    {
        InitializeComponent();
        ConfigPath.Text = App.State.ConfigPath;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = $"{AppIdentity.DisplayName} (WinUI) {version}";

        var automatic = SshLauncher.Resolve(null) == DevWorkspaceLauncher.SshClient.WezTerm ? "WezTerm" : "Windows Terminal, WezTerm is not installed";
        SshClient.ItemsSource = new[] { $"Automatic ({automatic})", "WezTerm", "Windows Terminal" };
        SshClient.SelectedIndex = Array.IndexOf(SshClients, App.State.Config.SshClient);
    }

    private void SshClient_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var chosen = SshClients[Math.Max(0, SshClient.SelectedIndex)];
        if (chosen != App.State.Config.SshClient)
        {
            App.State.Config.SshClient = chosen;
            App.State.Save();
        }
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
