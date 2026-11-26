using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    /// <summary>The SSH client choices, in the order of the combo box; null is "automatic".</summary>
    private static readonly SshClient?[] SshClients = [null, DevWorkspaceLauncher.SshClient.WezTerm, DevWorkspaceLauncher.SshClient.WindowsTerminal];

    /// <summary>The logon choices, in the order of the radio buttons.</summary>
    private static readonly StartupMode[] StartupModes = [StartupMode.Off, StartupMode.Tray, StartupMode.Window];

    private bool _showingStartup;

    public SettingsPage()
    {
        InitializeComponent();
        ConfigPath.Text = App.State.ConfigPath;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = $"{AppIdentity.DisplayName} (WinUI) {version}";

        var automatic = SshLauncher.Resolve(null) == DevWorkspaceLauncher.SshClient.WezTerm ? "WezTerm" : "Windows Terminal, WezTerm is not installed";
        SshClient.ItemsSource = new[] { $"Automatic ({automatic})", "WezTerm", "Windows Terminal" };
        SshClient.SelectedIndex = Array.IndexOf(SshClients, App.State.Config.SshClient);
        ShowStartup();
        // Monitors come and go while the app runs, so look again each time the page is shown.
        Loaded += (_, _) => ShowLayouts();
    }

    private List<WindowLayout> _layouts = [];

    /// <summary>Lists the recorded window layouts, the one for the monitors connected now first.</summary>
    private void ShowLayouts()
    {
        var (key, description) = WindowMover.CurrentMonitors();
        _layouts = WindowLayoutStore.Load()
            .OrderByDescending(layout => layout.Monitors == key)
            .ThenBy(layout => layout.Description, StringComparer.CurrentCulture)
            .ToList();
        var current = _layouts.Any(layout => layout.Monitors == key);
        CurrentMonitors.Text = $"Connected now: {description}" + (current ? "" : " - not recorded yet.");
        Layouts.ItemsSource = _layouts.Select(layout =>
                $"{layout.Description}{(layout.Monitors == key ? "  (connected now)" : "")} - " +
                $"{layout.Windows.Count} window(s), recorded {layout.Recorded:g}")
            .ToList();
        Layouts.Visibility = _layouts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreLayout.IsEnabled = current;
        DeleteLayout.IsEnabled = false;
    }

    private void Layouts_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteLayout.IsEnabled = Layouts.SelectedIndex >= 0;

    private void RecordLayout_Click(object sender, RoutedEventArgs e)
    {
        BuiltInActionRunner.Run(BuiltInFor(BuiltInActionId.RecordWindowPositions), App.State.LogMessage);
        ShowLayouts();
    }

    private void RestoreLayout_Click(object sender, RoutedEventArgs e) =>
        BuiltInActionRunner.Run(BuiltInFor(BuiltInActionId.RestoreWindowPositions), App.State.LogMessage);

    private void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (Layouts.SelectedIndex < 0)
        {
            return;
        }

        var layout = _layouts[Layouts.SelectedIndex];
        var layouts = WindowLayoutStore.Load();
        layouts.RemoveAll(existing => existing.Monitors == layout.Monitors);
        try
        {
            WindowLayoutStore.Save(layouts);
            App.State.LogMessage($"Deleted the window positions recorded for {layout.Description}.");
        }
        catch (Exception ex)
        {
            App.State.LogMessage($"Failed to delete the window positions: {ex.Message}");
        }

        ShowLayouts();
    }

    private static BuiltInAction BuiltInFor(BuiltInActionId id) =>
        App.State.Config.Actions.OfType<BuiltInAction>().FirstOrDefault(action => action.BuiltIn == id)
        ?? new BuiltInAction { Name = BuiltInActions.Describe(id).Name, BuiltIn = id };

    /// <summary>Shows how the Run key starts this copy, and which other copy it starts if any.</summary>
    private void ShowStartup()
    {
        var exe = Environment.ProcessPath;
        _showingStartup = true;
        Startup.SelectedIndex = Array.IndexOf(StartupModes, exe is null ? StartupMode.Off : StartupRegistration.Mode(exe));
        _showingStartup = false;

        var other = exe is null ? null : StartupRegistration.OtherExecutable(exe);
        StartupOther.Text = other is null ? string.Empty : $"Windows starts another copy at logon: {other}. Choose an option here to start this copy instead.";
        StartupOther.Visibility = other is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Startup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showingStartup && Startup.SelectedIndex >= 0)
        {
            TrayMenu.SetStartup(StartupModes[Startup.SelectedIndex]);
            ShowStartup();
        }
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
