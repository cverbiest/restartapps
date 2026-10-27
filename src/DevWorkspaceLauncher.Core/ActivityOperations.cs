namespace DevWorkspaceLauncher;

/// <summary>
/// Adding items to activities, and keeping activities in step when an app or RDP session is renamed or
/// removed - activities refer to them by name.
/// </summary>
public static class ActivityOperations
{
    public static Activity? Find(IEnumerable<Activity> activities, string? name) =>
        name is null
            ? null
            : activities.FirstOrDefault(activity => string.Equals(activity.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the app as Start. False when the activity already has it.</summary>
    public static bool AddApp(Activity activity, string name)
    {
        if (activity.Apps.Any(app => Same(app.Name, name)))
        {
            return false;
        }

        activity.Apps.Add(new ActivityApp { Name = name, Action = AppAction.Start });
        return true;
    }

    /// <summary>Adds the workspace as Open. False when the activity already has it.</summary>
    public static bool AddWorkspace(Activity activity, string uri)
    {
        if (activity.Workspaces.Any(workspace => Same(workspace.Uri, uri)))
        {
            return false;
        }

        activity.Workspaces.Add(new ActivityWorkspace { Uri = uri, Action = WorkspaceAction.Open });
        return true;
    }

    /// <summary>Adds the RDP session. False when the activity already has it.</summary>
    public static bool AddRdpSession(Activity activity, string name)
    {
        if (activity.RdpSessions.Any(session => Same(session.Name, name)))
        {
            return false;
        }

        activity.RdpSessions.Add(new ActivityRdpSession { Name = name });
        return true;
    }

    public static void RenameApp(ConfigurationFile config, string oldName, string newName)
    {
        foreach (var app in config.Activities.SelectMany(activity => activity.Apps).Where(app => Same(app.Name, oldName)))
        {
            app.Name = newName;
        }
    }

    public static void RenameRdpSession(ConfigurationFile config, string oldName, string newName)
    {
        foreach (var session in config.Activities.SelectMany(activity => activity.RdpSessions).Where(session => Same(session.Name, oldName)))
        {
            session.Name = newName;
        }
    }

    public static void RemoveApp(ConfigurationFile config, string name)
    {
        foreach (var activity in config.Activities)
        {
            activity.Apps.RemoveAll(app => Same(app.Name, name));
        }
    }

    public static void RemoveRdpSession(ConfigurationFile config, string name)
    {
        foreach (var activity in config.Activities)
        {
            activity.RdpSessions.RemoveAll(session => Same(session.Name, name));
        }
    }

    /// <summary>The name as given, or with " (2)", " (3)"... when an activity already has it.</summary>
    public static string UniqueName(IEnumerable<Activity> activities, string name, string suffix = "")
    {
        var taken = activities.Select(activity => activity.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }

        if (suffix.Length > 0 && !taken.Contains($"{name} ({suffix})"))
        {
            return $"{name} ({suffix})";
        }

        for (var number = 2; ; number++)
        {
            var candidate = suffix.Length > 0 ? $"{name} ({suffix} {number})" : $"{name} ({number})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
