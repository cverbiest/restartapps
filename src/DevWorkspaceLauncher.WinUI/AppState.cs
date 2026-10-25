using System.Collections.ObjectModel;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// What the window and its pages share: the configuration, the log, the busy flag and the session-only
/// workspace ticks. The pages and the navigation rail both run profiles, sessions and activities, so
/// those runs live here rather than on a page. The behaviour itself is Core's; this only wires it to
/// the UI, the same way the WinForms form does.
/// </summary>
public sealed class AppState
{
    private ConfigurationWatcher? _watcher;

    public ConfigurationFile Config { get; private set; } = new();

    public string ConfigPath => AppConfigurationStore.FilePath;

    /// <summary>Every log line, timestamped, oldest first.</summary>
    public ObservableCollection<string> Log { get; } = [];

    public bool Busy { get; private set; }

    /// <summary>The session picked in the Workspaces page's Session menu, kept across page visits.</summary>
    public string? SelectedSessionName { get; set; }

    /// <summary>The profile picked in the Apps page's Profile menu, kept across page visits.</summary>
    public string? SelectedProfileName { get; set; }

    /// <summary>Whether the Workspaces page shows every recent workspace rather than the preferred ones.</summary>
    public bool ShowAllWorkspaces { get; set; }

    /// <summary>
    /// The Close and Open ticks, by workspace URI. Held outside the list so a refresh - which happens
    /// whenever the window is activated - does not wipe a selection still being built.
    /// </summary>
    public Dictionary<string, (bool Close, bool Open)> WorkspaceTicks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised with each message as it is logged (without the timestamp).</summary>
    public event EventHandler<string>? MessageLogged;

    /// <summary>
    /// Raised after the configuration was reloaded from disk or saved from here - either way the rail
    /// and the pages show what is now in the file.
    /// </summary>
    public event EventHandler? ConfigurationChanged;

    public event EventHandler? BusyChanged;

    /// <summary>Raised when the app ticks changed outside the Apps page (a profile was loaded).</summary>
    public event EventHandler? AppsChanged;

    /// <summary>Raised when workspaces may have opened, closed or been re-ticked outside the Workspaces page.</summary>
    public event EventHandler? WorkspacesChanged;

    public event EventHandler? WindowActivated;

    public IReadOnlyList<AppConfiguration> VisibleApps => Config.Apps.Where(app => !app.Disabled).ToList();

    /// <summary>Loads the configuration (migrating or creating it first) and starts watching it. Call on the UI thread.</summary>
    public void Initialize()
    {
        try
        {
            var setupMessage = AppConfigurationStore.EnsureExists();
            var snapshot = AppConfigurationStore.Load();
            Config = snapshot.Configuration;
            if (setupMessage is not null)
            {
                LogMessage(setupMessage);
            }

            _watcher = new ConfigurationWatcher(ConfigPath) { LastKnownJson = snapshot.Json };
            _watcher.Reloaded += (_, reloaded) => ApplyReload(reloaded);
            _watcher.Failed += (_, message) => LogMessage(message);
            _watcher.Start();
            LogMessage($"Watching {ConfigPath} for changes.");
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to load configuration: {ex.Message}");
        }
    }

    public void LogMessage(string message)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss}: {message}");
        MessageLogged?.Invoke(this, message);
    }

    public void Save()
    {
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

    // ---------------------------------------------------------------- profiles

    public void ApplyProfile(Profile profile)
    {
        var unknown = ProfileOperations.Apply(profile, VisibleApps);
        AppsChanged?.Invoke(this, EventArgs.Empty);

        var suffix = unknown == 0 ? string.Empty : $" ({unknown} app(s) not in the profile were cleared)";
        LogMessage($"Loaded profile '{profile.Name}'.{suffix}");
    }

    /// <summary>The one-click profile: load its ticks, then Kill &amp; Start.</summary>
    public Task RunProfileAsync(Profile profile) =>
        RunBusyAsync(async () =>
        {
            ApplyProfile(profile);
            await AppController.KillAndStartAsync(VisibleApps, stopOnlySelected: false, LogMessage);
        });

    // ---------------------------------------------------------------- sessions

    /// <summary>Ticks Open for the session's workspaces and clears it everywhere else.</summary>
    public void ApplySession(WorkspaceProfile session)
    {
        SessionOperations.ApplyToTicks(session, WorkspaceTicks);
        var shown = WorkspaceController.LoadRows(Config, ShowAllWorkspaces, WorkspaceTicks).Count(row => row.Open);
        RaiseWorkspacesChanged();

        var hidden = session.Workspaces.Distinct(StringComparer.OrdinalIgnoreCase).Count() - shown;
        var suffix = hidden <= 0 ? string.Empty : $" ({hidden} not in the current list - Open Ticked still opens them)";
        LogMessage($"Loaded session '{session.Name}'.{suffix}");
    }

    /// <summary>The one-click session: tick it, then open its workspaces, including ones the filter hides.</summary>
    public Task RunSessionAsync(WorkspaceProfile session) =>
        RunBusyAsync(async () =>
        {
            ApplySession(session);
            var rows = WorkspaceController.LoadRows(Config, showAll: true, WorkspaceTicks);
            await WorkspaceController.OpenAsync(SessionOperations.ResolveNames(session, rows), Config.VsCodeExecutable, LogMessage);
            RaiseWorkspacesChanged();
        });

    // -------------------------------------------------------------- activities

    public Task RunActivityAsync(Activity activity) =>
        RunBusyAsync(async () =>
        {
            await ActivityRunner.RunAsync(activity, Config, LogMessage);
            RaiseWorkspacesChanged();
        });

    private void ApplyReload(ConfigurationSnapshot snapshot)
    {
        Config = snapshot.Configuration;
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);

        var visible = VisibleApps.Count;
        var hidden = Config.Apps.Count - visible;
        var hiddenText = hidden == 0 ? string.Empty : $", {hidden} disabled";
        LogMessage($"Configuration reloaded ({visible} application(s){hiddenText}, {Config.Profiles.Count} profile(s), {Config.Activities.Count} activity(ies)).");
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
}
