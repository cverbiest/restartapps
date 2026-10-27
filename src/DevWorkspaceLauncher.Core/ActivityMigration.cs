namespace DevWorkspaceLauncher;

/// <summary>
/// Turns app profiles and workspace sessions into activities, which do the same and more. Done once,
/// when the configuration still has either; the caller keeps a copy of the file first.
/// </summary>
public static class ActivityMigration
{
    public static bool IsNeeded(ConfigurationFile config) =>
        config.Profiles.Count > 0 || config.WorkspaceProfiles.Count > 0;

    /// <summary>
    /// Moves every profile and workspace session into <see cref="ConfigurationFile.Activities"/> and
    /// empties both lists. Returns a line per converted item for the log.
    /// </summary>
    public static List<string> Convert(ConfigurationFile config)
    {
        var messages = new List<string>();

        foreach (var profile in config.Profiles)
        {
            // Kill and Restart = restart; Kill alone = stop; Restart alone = start. A profile also left
            // every app it does not list alone, which is what an activity does anyway.
            var activity = new Activity
            {
                Name = ActivityOperations.UniqueName(config.Activities, profile.Name, "profile"),
                Favorite = profile.Favorite,
                Apps = profile.Entries
                    .Where(entry => entry.Kill || entry.Restart)
                    .Select(entry => new ActivityApp
                    {
                        Name = entry.Name,
                        Action = entry is { Kill: true, Restart: true } ? AppAction.Restart
                            : entry.Kill ? AppAction.Stop
                            : AppAction.Start
                    })
                    .ToList()
            };
            config.Activities.Add(activity);
            messages.Add($"App profile '{profile.Name}' is now activity '{activity.Name}'.");
        }

        foreach (var session in config.WorkspaceProfiles)
        {
            var activity = new Activity
            {
                Name = ActivityOperations.UniqueName(config.Activities, session.Name, "session"),
                Favorite = session.Favorite,
                Workspaces = session.Workspaces
                    .Select(uri => new ActivityWorkspace { Uri = uri, Action = WorkspaceAction.Open })
                    .ToList()
            };
            config.Activities.Add(activity);
            messages.Add($"Workspace session '{session.Name}' is now activity '{activity.Name}'.");
        }

        config.Profiles.Clear();
        config.WorkspaceProfiles.Clear();
        return messages;
    }
}
