namespace DevWorkspaceLauncher;

public enum ActivityStepKind
{
    StopApp,
    CloseWorkspace,
    Wait,
    StartApp,
    OpenWorkspace,
    ConnectRdp,
    ConnectSsh,
    RunBuiltIn
}

/// <summary>
/// One thing a run does. <see cref="App"/> is set for application steps, <see cref="Uri"/> for workspace
/// steps, <see cref="RdpSession"/> for RDP steps, <see cref="BuiltIn"/> for built-in actions; an SSH
/// step's <see cref="Name"/> is the host's alias.
/// </summary>
public sealed record ActivityStep(
    ActivityStepKind Kind,
    string Name,
    ApplicationAction? App = null,
    string? Uri = null,
    string? WindowTitle = null,
    RdpSession? RdpSession = null,
    BuiltInAction? BuiltIn = null);

/// <summary>The steps of a run, in order, and the lines to log about items that were skipped.</summary>
public sealed record ActivityPlan(IReadOnlyList<ActivityStep> Steps, IReadOnlyList<string> Notes);

/// <summary>
/// Decides what running an activity does, without doing any of it, so the rules can be tested.
/// <para>
/// Order: every stop and close first, a pause if anything was stopped, then every start and open, then
/// the RDP connections, then the SSH connections, then the built-in actions, each group in the order the
/// activity lists them - the same shape as Kill &amp;&amp; Start. Built-in actions come last so that, say,
/// moving windows also moves the ones the activity opened.
/// </para>
/// </summary>
public static class ActivityPlanner
{
    /// <param name="sshAliases">The hosts in <c>~/.ssh/config</c>; null when the activity has no SSH hosts to look up.</param>
    public static ActivityPlan Plan(
        Activity activity,
        ConfigurationFile config,
        Func<ApplicationAction, bool> isRunning,
        IReadOnlyCollection<string>? sshAliases = null)
    {
        var stops = new List<ActivityStep>();
        var closes = new List<ActivityStep>();
        var starts = new List<ActivityStep>();
        var opens = new List<ActivityStep>();
        var connects = new List<ActivityStep>();
        var builtIns = new List<ActivityStep>();
        var notes = new List<string>();

        foreach (var item in activity.Actions)
        {
            var action = config.Actions.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, item.Name, StringComparison.OrdinalIgnoreCase));

            if (action is null)
            {
                notes.Add($"No action named '{item.Name}'; skipped.");
                continue;
            }

            if (!action.Verbs.Contains(item.Action))
            {
                notes.Add($"{action.Name} cannot {item.Action}; skipped.");
                continue;
            }

            if (action is BuiltInAction builtIn)
            {
                builtIns.Add(new ActivityStep(ActivityStepKind.RunBuiltIn, builtIn.Name, BuiltIn: builtIn));
                continue;
            }

            var app = (ApplicationAction)action;
            if (app.Disabled)
            {
                notes.Add($"{app.Name} is disabled; skipped.");
                continue;
            }

            switch (item.Action)
            {
                case ActionVerb.Stop when !app.CanStop:
                    notes.Add($"{app.Name} cannot be stopped (StopMethod None); skipped.");
                    break;

                case ActionVerb.Stop:
                    stops.Add(new ActivityStep(ActivityStepKind.StopApp, app.Name, App: app));
                    break;

                case ActionVerb.Restart when app.CanStop:
                    stops.Add(new ActivityStep(ActivityStepKind.StopApp, app.Name, App: app));
                    starts.Add(new ActivityStep(ActivityStepKind.StartApp, app.Name, App: app));
                    break;

                case ActionVerb.Restart:
                    notes.Add($"{app.Name} cannot be stopped (StopMethod None); starting it if not running.");
                    AddStartIfNotRunning(app);
                    break;

                default:
                    AddStartIfNotRunning(app);
                    break;
            }
        }

        foreach (var item in activity.Workspaces)
        {
            if (!IsWorkspaceUri(item.Uri))
            {
                notes.Add($"'{item.Uri}' is not a workspace URI; skipped.");
                continue;
            }

            var favorite = config.Workspaces.FirstOrDefault(candidate =>
                string.Equals(candidate.Uri, item.Uri, StringComparison.OrdinalIgnoreCase));
            var name = favorite?.Name ?? VsCodeUri.Describe(item.Uri).DisplayName;

            if (item.Action == WorkspaceAction.Close)
            {
                closes.Add(new ActivityStep(ActivityStepKind.CloseWorkspace, name, Uri: item.Uri, WindowTitle: favorite?.WindowTitle));
            }
            else
            {
                opens.Add(new ActivityStep(ActivityStepKind.OpenWorkspace, name, Uri: item.Uri, WindowTitle: favorite?.WindowTitle));
            }
        }

        foreach (var item in activity.RdpSessions)
        {
            var session = config.RdpSessions.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (session is null)
            {
                notes.Add($"No RDP session named '{item.Name}'; skipped.");
                continue;
            }

            connects.Add(new ActivityStep(ActivityStepKind.ConnectRdp, session.Name, RdpSession: session));
        }

        foreach (var item in activity.SshHosts)
        {
            var alias = sshAliases?.FirstOrDefault(candidate => string.Equals(candidate, item.Name, StringComparison.OrdinalIgnoreCase));
            if (alias is null)
            {
                notes.Add($"No SSH host '{item.Name}' in ~/.ssh/config; skipped.");
                continue;
            }

            connects.Add(new ActivityStep(ActivityStepKind.ConnectSsh, alias));
        }

        var steps = new List<ActivityStep>();
        steps.AddRange(stops);
        steps.AddRange(closes);
        if (stops.Count > 0)
        {
            steps.Add(new ActivityStep(ActivityStepKind.Wait, string.Empty));
        }

        steps.AddRange(starts);
        steps.AddRange(opens);
        steps.AddRange(connects);
        steps.AddRange(builtIns);

        if (steps.Count == 0)
        {
            notes.Add("Nothing to do.");
        }

        return new ActivityPlan(steps, notes);

        // VS Code stores every workspace as a URI with a scheme - file://, vscode-remote:// - so a
        // plain path or a blank entry is a typo in the file, not something to hand to VS Code.
        static bool IsWorkspaceUri(string? uri) =>
            !string.IsNullOrWhiteSpace(uri) && uri.Contains("://", StringComparison.Ordinal);

        void AddStartIfNotRunning(ApplicationAction app)
        {
            if (isRunning(app))
            {
                notes.Add($"{app.Name} is already running.");
            }
            else
            {
                starts.Add(new ActivityStep(ActivityStepKind.StartApp, app.Name, App: app));
            }
        }
    }
}
