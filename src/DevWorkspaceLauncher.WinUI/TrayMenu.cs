namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// What the tray menu offers, rebuilt each time it opens: one level with Open launcher, the favorites
/// (the start page's items), then the app's own commands.
/// </summary>
internal static class TrayMenu
{
    private static AppState State => App.State;

    public static IReadOnlyList<TrayEntry> Build()
    {
        var entries = new List<TrayEntry>
        {
            new("Open launcher", App.ShowMainWindow) { Default = true },
            TrayEntry.Separator
        };

        State.ReloadSshHosts();
        var favorites = LaunchItem.Favorites(State.Config).ToList();
        entries.AddRange(favorites.Count > 0
            ? favorites.Select(item => new TrayEntry(item.Name, item.Run))
            : [new TrayEntry("No favorites yet: pin items in the launcher") { Enabled = false }]);

        entries.Add(TrayEntry.Separator);
        entries.AddRange(WindowPositionEntries());
        entries.Add(new TrayEntry("Start with Windows", ToggleStartup) { Checked = IsStartupEnabled() });
        entries.Add(new TrayEntry("Exit", App.ExitApp));
        return entries;
    }

    /// <summary>
    /// Record and Restore window positions, unless pinned (then they are among the favorites already).
    /// Restore is greyed out while nothing is recorded for the monitors connected now.
    /// </summary>
    private static IEnumerable<TrayEntry> WindowPositionEntries()
    {
        var (key, _) = WindowMover.CurrentMonitors();
        var recorded = WindowLayoutStore.Load().Any(layout => layout.Monitors == key);
        foreach (var action in State.Config.Actions.OfType<BuiltInAction>())
        {
            if (action.Favorite || action.BuiltIn is not (BuiltInActionId.RecordWindowPositions or BuiltInActionId.RestoreWindowPositions))
            {
                continue;
            }

            yield return new TrayEntry(action.Name, () => State.RunAction(action))
            {
                Enabled = action.BuiltIn == BuiltInActionId.RecordWindowPositions || recorded
            };
        }
    }

    /// <summary>Whether a log line reports a problem - the same test as the window's status banner.</summary>
    public static bool IsProblem(string message) =>
        message.StartsWith("Failed", StringComparison.Ordinal)
        || message.StartsWith("Error", StringComparison.Ordinal)
        || message.StartsWith("Could not", StringComparison.Ordinal);

    private static bool IsStartupEnabled() =>
        Environment.ProcessPath is { } exe && StartupRegistration.Mode(exe) != StartupMode.Off;

    /// <summary>Turns starting at logon off, or on in the tray (the Settings page also offers the window).</summary>
    private static void ToggleStartup() =>
        SetStartup(IsStartupEnabled() ? StartupMode.Off : StartupMode.Tray);

    /// <summary>Starts this copy of the app at logon as <paramref name="mode"/> says, or stops doing so.</summary>
    public static void SetStartup(StartupMode mode)
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            StartupRegistration.Set(mode, exe);
            State.LogMessage(mode switch
            {
                StartupMode.Tray => "Starts with Windows, in the tray.",
                StartupMode.Window => "Starts with Windows, with the window open.",
                _ => "No longer starts with Windows."
            });
        }
        catch (Exception ex)
        {
            State.LogMessage($"Failed to change Start with Windows: {ex.Message}");
        }
    }
}
