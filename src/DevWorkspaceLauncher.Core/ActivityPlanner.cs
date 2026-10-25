namespace DevWorkspaceLauncher;

public enum ActivityStepKind
{
    StopApp,
    CloseWorkspace,
    Wait,
    StartApp,
    OpenWorkspace
}

/// <summary>One thing a run does. <see cref="App"/> is set for app steps, <see cref="Uri"/> for workspace steps.</summary>
public sealed record ActivityStep(
    ActivityStepKind Kind,
    string Name,
    AppConfiguration? App = null,
    string? Uri = null,
    string? WindowTitle = null);

/// <summary>The steps of a run, in order, and the lines to log about items that were skipped.</summary>
public sealed record ActivityPlan(IReadOnlyList<ActivityStep> Steps, IReadOnlyList<string> Notes);

/// <summary>
/// Decides what running an activity does, without doing any of it, so the rules can be tested.
/// <para>
/// Order: every stop and close first, a pause if anything was stopped, then every start and open, each
/// group in the order the activity lists them - the same shape as Kill &amp;&amp; Start.
/// </para>
/// </summary>
public static class ActivityPlanner
{
    public static ActivityPlan Plan(Activity activity, ConfigurationFile config, Func<AppConfiguration, bool> isRunning)
    {
        var stops = new List<ActivityStep>();
        var closes = new List<ActivityStep>();
        var starts = new List<ActivityStep>();
        var opens = new List<ActivityStep>();
        var notes = new List<string>();

        foreach (var item in activity.Apps)
        {
            var app = config.Apps.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, item.Name, StringComparison.OrdinalIgnoreCase));

            if (app is null)
            {
                notes.Add($"No app named '{item.Name}'; skipped.");
                continue;
            }

            if (app.Disabled)
            {
                notes.Add($"{app.Name} is disabled; skipped.");
                continue;
            }

            switch (item.Action)
            {
                case AppAction.Stop when !app.CanStop:
                    notes.Add($"{app.Name} cannot be stopped (StopMethod None); skipped.");
                    break;

                case AppAction.Stop:
                    stops.Add(new ActivityStep(ActivityStepKind.StopApp, app.Name, App: app));
                    break;

                case AppAction.Restart when app.CanStop:
                    stops.Add(new ActivityStep(ActivityStepKind.StopApp, app.Name, App: app));
                    starts.Add(new ActivityStep(ActivityStepKind.StartApp, app.Name, App: app));
                    break;

                case AppAction.Restart:
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

        var steps = new List<ActivityStep>();
        steps.AddRange(stops);
        steps.AddRange(closes);
        if (stops.Count > 0)
        {
            steps.Add(new ActivityStep(ActivityStepKind.Wait, string.Empty));
        }

        steps.AddRange(starts);
        steps.AddRange(opens);

        if (steps.Count == 0)
        {
            notes.Add("Nothing to do.");
        }

        return new ActivityPlan(steps, notes);

        void AddStartIfNotRunning(AppConfiguration app)
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
