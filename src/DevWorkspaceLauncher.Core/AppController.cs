using System.Diagnostics;

namespace DevWorkspaceLauncher;

/// <summary>Stops, starts and checks the configured applications.</summary>
public static class AppController
{
    /// <summary>
    /// Whether the app is running now: for <see cref="StopMethod.Window"/>, whether a window with its
    /// AppUserModelID is open (a PWA has no process of its own); otherwise whether a process with the
    /// command line's executable name exists.
    /// </summary>
    public static bool IsRunning(AppConfiguration app)
    {
        if (app.StopMethod == StopMethod.Window)
        {
            return !string.IsNullOrWhiteSpace(app.WindowAumid) && WindowFinder.FindByAumid(app.WindowAumid).Count > 0;
        }

        var processName = CommandLineParser.GetProcessName(app.ExpandedCommandline);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>
    /// Stops the apps ticked Kill (or, with <paramref name="stopOnlySelected"/>, the ones ticked Restart),
    /// waits a second, then starts the ones ticked Restart.
    /// </summary>
    public static async Task KillAndStartAsync(IReadOnlyList<AppConfiguration> visibleApps, bool stopOnlySelected, Action<string> log)
    {
        var appsToStart = visibleApps.Where(app => app.Restart).ToList();
        var appsToStop = stopOnlySelected
            ? appsToStart.Where(app => app.CanStop).ToList()
            : visibleApps.Where(app => app.Kill && app.CanStop).ToList();

        if (appsToStop.Count == 0 && appsToStart.Count == 0)
        {
            log(stopOnlySelected
                ? "No applications selected."
                : "Nothing to do: no application is ticked Kill or Restart.");
            return;
        }

        try
        {
            if (appsToStop.Count == 0)
            {
                log("Nothing to stop.");
            }
            else
            {
                log(stopOnlySelected ? "STOPPING selected applications..." : "STOPPING applications...");
                foreach (var app in appsToStop)
                {
                    await StopAsync(app, log);
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            if (appsToStart.Count == 0)
            {
                log("No applications ticked Restart; nothing to start.");
            }
            else
            {
                log("STARTING selected applications...");
                foreach (var app in appsToStart)
                {
                    Start(app, log);
                }
            }

            log("Operation Complete.");
        }
        catch (Exception ex)
        {
            log($"Error: {ex.Message}");
        }
    }

    public static async Task StopAsync(AppConfiguration app, Action<string> log)
    {
        switch (app.StopMethod)
        {
            case StopMethod.None:
                log($"Skipping {app.Name}: StopMethod is None.");
                return;

            case StopMethod.Window:
                await CloseWindowsAsync(app, log);
                return;

            default:
                var processName = CommandLineParser.GetProcessName(app.ExpandedCommandline);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    log($"Skipping {app.Name}: no executable found in the command line.");
                    return;
                }

                log($"Stopping {app.Name} ({processName})...");
                StopProcesses(processName);
                return;
        }
    }

    /// <summary>
    /// The one-click app: brings its window to the front when it is running, starts it otherwise.
    /// Never stops anything.
    /// </summary>
    public static void StartOrFocus(AppConfiguration app, Action<string> log)
    {
        if (!IsRunning(app))
        {
            Start(app, log);
            return;
        }

        var windows = app.StopMethod == StopMethod.Window && !string.IsNullOrWhiteSpace(app.WindowAumid)
            ? WindowFinder.FindByAumid(app.WindowAumid)
            : WindowFinder.FindByProcessName(CommandLineParser.GetProcessName(app.ExpandedCommandline));
        if (windows.Count == 0)
        {
            log($"{app.Name} is running but has no window to bring to the front.");
            return;
        }

        log($"Bringing {app.Name} to the front.");
        WindowFinder.BringToFront(windows[0].Handle);
    }

    public static void Start(AppConfiguration app, Action<string> log)
    {
        log($"Starting {app.Name}...");
        try
        {
            StartCommand(app.ExpandedCommandline);
        }
        catch (Exception ex)
        {
            log($"Failed to start {app.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Closes the windows matching the app's AppUserModelID. Used for browser-hosted PWAs, which
    /// share their process with every other window of that browser.
    /// </summary>
    private static async Task CloseWindowsAsync(AppConfiguration app, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(app.WindowAumid))
        {
            log($"Skipping {app.Name}: StopMethod is Window but WindowAumid is not set.");
            return;
        }

        var windows = WindowFinder.FindByAumid(app.WindowAumid);
        if (windows.Count == 0)
        {
            log($"{app.Name}: no window matching '{app.WindowAumid}' is open.");
            return;
        }

        log($"Closing {app.Name} ({windows.Count} window(s) matching '{app.WindowAumid}')...");
        foreach (var window in windows)
        {
            WindowFinder.Close(window.Handle);
        }

        // Give the app a moment to take the close down cleanly.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (windows.All(window => !WindowFinder.StillOpen(window.Handle)))
            {
                log($"{app.Name}: closed.");
                return;
            }

            await Task.Delay(150);
        }

        var remaining = windows.Count(window => WindowFinder.StillOpen(window.Handle));
        log($"{app.Name}: {remaining} window(s) did not close within 3 seconds.");
    }

    private static void StopProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Match the PowerShell prototype's SilentlyContinue behavior.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void StartCommand(string commandline)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" {commandline}",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
