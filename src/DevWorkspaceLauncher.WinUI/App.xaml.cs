using Microsoft.UI.Xaml;

namespace DevWorkspaceLauncher.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>The state shared by the window and its pages.</summary>
    public static AppState State { get; } = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Here, on the UI thread, so the configuration watcher reports back on it.
        State.Initialize();

        _window = new MainWindow();
        WindowMessageHook.Attach(_window, OnWindowMessage);
        _window.Activate();
    }

    private static void OnWindowMessage(uint message, IntPtr lParam)
    {
        if (!UserEnvironment.IsEnvironmentChange(message, lParam))
        {
            return;
        }

        try
        {
            UserEnvironment.Refresh();
            State.LogMessage("Environment variables changed; apps started from now on get the new values.");
        }
        catch (Exception ex)
        {
            State.LogMessage($"Failed to reload environment variables: {ex.Message}");
        }
    }
}
