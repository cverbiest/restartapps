using Microsoft.UI.Xaml;

namespace DevWorkspaceLauncher.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // An error escaping an event handler would otherwise end the app; log it and carry on.
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            State.LogMessage($"Error: {e.Exception.Message}");
        };
    }

    /// <summary>The state shared by the window and its pages.</summary>
    public static AppState State { get; } = new();

    /// <summary>The main window, for pickers that need its handle. Null when run as a command line.</summary>
    public static Window? MainWindow => ((App)Current)._window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (commandLine.Length > 0 && string.Equals(commandLine[0], "rdp", StringComparison.OrdinalIgnoreCase))
        {
            RunRdpCommandLine(commandLine[1..]);
            return;
        }

        // Here, on the UI thread, so the configuration watcher reports back on it.
        State.Initialize();

        _window = new MainWindow();
        WindowMessageHook.Attach(_window, OnWindowMessage);
        _window.Activate();
    }

    /// <summary>
    /// <c>DevWorkspaceLauncher.WinUI.exe rdp /switches</c>: connects without opening a window and exits
    /// with the launch's exit code. A failure shows a message box unless <c>/batch</c> is given.
    /// </summary>
    private static async void RunRdpCommandLine(string[] args)
    {
        var (exitCode, message) = await RdpCommand.RunAsync(args, () => AppConfigurationStore.Load().Configuration, _ => { });
        if (message is not null && !RdpCommand.IsBatch(args))
        {
            MessageBox(IntPtr.Zero, message, AppIdentity.DisplayName, 0x10 /* MB_ICONERROR */);
        }

        Environment.Exit(exitCode);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

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
