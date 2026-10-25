namespace DevWorkspaceLauncher;

/// <summary>The rules for sessions: named sets of workspaces to open together. Sessions never close anything.</summary>
public static class SessionOperations
{
    public static WorkspaceProfile? Find(IEnumerable<WorkspaceProfile> sessions, string? name) =>
        name is null
            ? null
            : sessions.FirstOrDefault(session => string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ticks Open for the session's workspaces and clears it everywhere else. Close ticks are kept.</summary>
    public static void ApplyToTicks(WorkspaceProfile session, IDictionary<string, (bool Close, bool Open)> ticks)
    {
        var wanted = new HashSet<string>(session.Workspaces, StringComparer.OrdinalIgnoreCase);

        foreach (var uri in ticks.Keys.ToList())
        {
            ticks[uri] = (ticks[uri].Close, wanted.Contains(uri));
        }

        foreach (var uri in wanted)
        {
            ticks.TryGetValue(uri, out var existing);
            ticks[uri] = (existing.Close, true);
        }
    }

    /// <summary>
    /// The session's workspaces with the names to show for them: the row's name where the workspace is
    /// listed, the name from its URI otherwise - so a workspace hidden by the current filter still opens.
    /// </summary>
    public static List<(string Uri, string Name)> ResolveNames(WorkspaceProfile session, IEnumerable<WorkspaceRow> rows)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            names[row.Uri] = row.Name;
        }

        return session.Workspaces
            .Select(uri => (uri, names.TryGetValue(uri, out var name) ? name : VsCodeUri.Describe(uri).DisplayName))
            .ToList();
    }

    /// <summary>Replaces the workspaces of the session with this name, or adds a new session.</summary>
    public static WorkspaceProfile Upsert(List<WorkspaceProfile> sessions, string name, List<string> uris)
    {
        var existing = Find(sessions, name);
        if (existing is not null)
        {
            existing.Workspaces = uris;
            return existing;
        }

        var session = new WorkspaceProfile { Name = name, Workspaces = uris };
        sessions.Add(session);
        return session;
    }
}
