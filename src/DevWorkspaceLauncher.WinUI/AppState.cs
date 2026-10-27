using System.Collections.ObjectModel;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// What the window and its pages share: the configuration, the log and the busy flag. The pages, the
/// start page and the tray all run activities, apps and RDP sessions, so those runs live here rather
/// than on a page. The behaviour itself is Core's; this only wires it to the UI.
/// </summary>
public sealed class AppState
{
    private ConfigurationWatcher? _watcher;

    /// <summary>Whether <see cref="Config"/> came from the file. Until it has, nothing may be saved.</summary>
    private bool _loaded;

    public ConfigurationFile Config { get; private set; } = new();

    public string ConfigPath => AppConfigurationStore.FilePath;

    /// <summary>Every log line, timestamped, oldest first.</summary>
    public ObservableCollection<string> Log { get; } = [];

    public bool Busy { get; private set; }

    /// <summary>Whether the Workspaces page shows every recent workspace rather than the preferred ones.</summary>
    public bool ShowAllWorkspaces { get; set; }

    /// <summary>Raised with each message as it is logged (without the timestamp).</summary>
    public event EventHandler<string>? MessageLogged;

    /// <summary>
    /// Raised after the configuration was reloaded from disk or saved from here - either way the rail
    /// and the pages show what is now in the file.
    /// </summary>
    public event EventHandler? ConfigurationChanged;

    public event EventHandler? BusyChanged;

    /// <summary>Raised when workspaces may have opened, closed or been re-ticked outside the Workspaces page.</summary>
    public event EventHandler? WorkspacesChanged;

    public event EventHandler? WindowActivated;

    public IReadOnlyList<AppConfiguration> VisibleApps => Config.Apps.Where(app => !app.Disabled).ToList();

    /// <summary>
    /// Loads the configuration (migrating or creating it first) and starts watching it. Call on the UI
    /// thread. A file that fails to load is still watched, so fixing it brings the app back.
    /// </summary>
    public void Initialize()
    {
        var lastKnownJson = string.Empty;
        try
        {
            var setupMessage = AppConfigurationStore.EnsureExists();
            var snapshot = AppConfigurationStore.Load();
            Config = snapshot.Configuration;
            lastKnownJson = snapshot.Json;
            _loaded = true;
            if (setupMessage is not null)
            {
                LogMessage(setupMessage);
            }

            if (ConvertToActivities())
            {
                lastKnownJson = AppConfigurationStore.ToJson(Config);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to load configuration: {ex.Message} Nothing will be saved until the file loads; fix it and it reloads.");
        }

        try
        {
            _watcher = new ConfigurationWatcher(ConfigPath) { LastKnownJson = lastKnownJson };
            _watcher.Reloaded += (_, reloaded) => ApplyReload(reloaded);
            _watcher.Failed += (_, message) => LogMessage(message);
            _watcher.Start();
            LogMessage($"Watching {ConfigPath} for changes.");
        }
        catch (Exception ex)
        {
            LogMessage($"Could not watch the configuration file: {ex.Message}");
        }
    }

    public void LogMessage(string message)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss}: {message}");
        MessageLogged?.Invoke(this, message);
    }

    /// <summary>
    /// Writes the configuration back. Refused while the file has never loaded: what is in memory then is
    /// an empty stand-in, and saving it would wipe every app, profile, session and activity in the file.
    /// </summary>
    public void Save()
    {
        if (!_loaded)
        {
            LogMessage($"Not saved: {ConfigPath} did not load. Fix the file - it reloads automatically - and try again.");
            return;
        }

        try
        {
            var json = AppConfigurationStore.Save(Config);
            if (_watcher is not null)
            {
                _watcher.LastKnownJson = json;
            }

            LogMessage($"Saved {ConfigPath}.");
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to save configuration: {ex.Message}");
        }
    }

    public void RaiseWindowActivated() => WindowActivated?.Invoke(this, EventArgs.Empty);

    public void RaiseWorkspacesChanged() => WorkspacesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs <paramref name="work"/> unless something else is running, with <see cref="Busy"/> set meanwhile.</summary>
    public async Task RunBusyAsync(Func<Task> work)
    {
        if (Busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await work();
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------------- apps

    /// <summary>Starts the apps that are not running.</summary>
    public Task StartAppsAsync(IReadOnlyList<AppConfiguration> apps) =>
        RunBusyAsync(() =>
        {
            foreach (var app in apps)
            {
                if (AppController.IsRunning(app))
                {
                    LogMessage($"{app.Name} is already running.");
                }
                else
                {
                    AppController.Start(app, LogMessage);
                }
            }

            return Task.CompletedTask;
        });

    public Task StopAppsAsync(IReadOnlyList<AppConfiguration> apps) =>
        RunBusyAsync(async () =>
        {
            foreach (var app in apps)
            {
                await AppController.StopAsync(app, LogMessage);
            }
        });

    /// <summary>Stops the apps, waits a second, starts them again - the same shape as an activity's Restart.</summary>
    public Task RestartAppsAsync(IReadOnlyList<AppConfiguration> apps) =>
        RunBusyAsync(async () =>
        {
            foreach (var app in apps.Where(app => app.CanStop))
            {
                await AppController.StopAsync(app, LogMessage);
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            foreach (var app in apps)
            {
                // One that cannot be stopped is still running; starting it again would make a second one.
                if (!app.CanStop && AppController.IsRunning(app))
                {
                    LogMessage($"{app.Name} cannot be stopped (StopMethod None) and is already running.");
                }
                else
                {
                    AppController.Start(app, LogMessage);
                }
            }
        });

    /// <summary>The one-click app: start it, or bring it to the front when it is running.</summary>
    public void StartOrFocusApp(AppConfiguration app)
    {
        try
        {
            AppController.StartOrFocus(app, LogMessage);
        }
        catch (Exception ex)
        {
            LogMessage($"Could not start {app.Name}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------- activities

    public Task RunActivityAsync(Activity activity) =>
        RunBusyAsync(async () =>
        {
            await ActivityRunner.RunAsync(activity, Config, LogMessage);
            RaiseWorkspacesChanged();
        });

    // ------------------------------------------------------------ RDP sessions

    /// <summary>Starts mstsc for a saved session. The clean-up runs on after it, while this app lives.</summary>
    public void ConnectRdp(RdpSession session)
    {
        try
        {
            RdpLauncher.Launch(RdpResolver.Resolve(Config, session), session.Name, LogMessage);
        }
        catch (Exception ex)
        {
            LogMessage($"Could not connect to '{session.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Turns app profiles and workspace sessions into activities, once: after a copy of the file is
    /// kept next to it, the converted configuration is saved. Also catches a file edited by hand, or by
    /// the WinForms app, that has them again.
    /// </summary>
    private bool ConvertToActivities()
    {
        if (!ActivityMigration.IsNeeded(Config))
        {
            return false;
        }

        try
        {
            var backup = $"{ConfigPath}.{DateTime.Now:yyyyMMdd-HHmmss}.before-activities";
            File.Copy(ConfigPath, backup, overwrite: false);
            foreach (var message in ActivityMigration.Convert(Config))
            {
                LogMessage(message);
            }

            LogMessage($"Kept the configuration from before the conversion as {backup}.");
            Save();
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to convert profiles and sessions into activities: {ex.Message}");
            return false;
        }
    }

    private void ApplyReload(ConfigurationSnapshot snapshot)
    {
        Config = snapshot.Configuration;
        _loaded = true;
        ConvertToActivities();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);

        var visible = VisibleApps.Count;
        var hidden = Config.Apps.Count - visible;
        var hiddenText = hidden == 0 ? string.Empty : $", {hidden} disabled";
        LogMessage($"Configuration reloaded ({visible} application(s){hiddenText}, {Config.Activities.Count} activity(ies), {Config.RdpSessions.Count} RDP session(s)).");
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
}
