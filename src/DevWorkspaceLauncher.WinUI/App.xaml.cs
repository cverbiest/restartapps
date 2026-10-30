using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DevWorkspaceLauncher.WinUI;

public partial class App : Application
{
    private Window? _window;
    private TrayIcon? _tray;

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

    /// <summary>Set by Exit in the tray menu: closing the window then ends the app instead of hiding it.</summary>
    public static bool IsExiting { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (commandLine.Length > 0 && string.Equals(commandLine[0], "rdp", StringComparison.OrdinalIgnoreCase))
        {
            RunRdpCommandLine(commandLine[1..]);
            return;
        }

        var startInTray = commandLine.Any(arg => string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase));

        // One instance, with one tray icon: starting the app again shows the running one instead.
        var main = AppInstance.FindOrRegisterForKey(AppIdentity.CodeName);
        if (!main.IsCurrent)
        {
            if (!startInTray)
            {
                await main.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            }

            Environment.Exit(0);
            return;
        }

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        main.Activated += (_, _) => dispatcher.TryEnqueue(ShowMainWindow);

        // Here, on the UI thread, so the configuration watcher reports back on it.
        State.Initialize();

        _window = new MainWindow();
        WindowMessageHook.Attach(_window, OnWindowMessage);

        _tray = new TrayIcon(AppIdentity.DisplayName, TrayMenu.Build);
        State.MessageLogged += (_, message) =>
        {
            // While only the tray is showing, a failed launch would go unnoticed in the log.
            if (_window.AppWindow.IsVisible == false && TrayMenu.IsProblem(message))
            {
                _tray.ShowWarning(AppIdentity.DisplayName, message);
            }
        };

        if (startInTray)
        {
            State.LogMessage("Started in the tray.");
        }
        else
        {
            _window.Activate();
        }
    }

    /// <summary>Shows the main window, from the tray or a second start of the app.</summary>
    public static void ShowMainWindow()
    {
        if (MainWindow is { } window)
        {
            window.AppWindow.Show();
            window.Activate();
        }
    }

    /// <summary>Exit from the tray menu: removes the icon and ends the app.</summary>
    public static void ExitApp()
    {
        IsExiting = true;
        var app = (App)Current;
        app._tray?.Dispose();
        app._tray = null;
        app._window?.Close();
        app.Exit();
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
