namespace DevWorkspaceLauncher.Tests;

public class ActivityPlannerTests
{
    private const string U1 = "file:///c%3A/work/one";
    private const string U2 = "file:///c%3A/work/two";

    private readonly ConfigurationFile _config = new()
    {
        Apps =
        [
            new AppConfiguration { Name = "A", Commandline = "a.exe" },
            new AppConfiguration { Name = "B", Commandline = "b.exe" },
            new AppConfiguration { Name = "P", Commandline = "brave.exe", StopMethod = StopMethod.Window, WindowAumid = "*_crx_*" },
            new AppConfiguration { Name = "N", Commandline = "n.exe", StopMethod = StopMethod.None },
            new AppConfiguration { Name = "D", Commandline = "d.exe", Disabled = true }
        ],
        Workspaces = [new WorkspaceFavorite { Name = "One (pinned)", Uri = U1, WindowTitle = "one*" }]
    };

    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    private ActivityPlan Plan(ActivityApp[]? apps = null, ActivityWorkspace[]? workspaces = null) =>
        ActivityPlanner.Plan(
            new Activity { Name = "X", Apps = [.. apps ?? []], Workspaces = [.. workspaces ?? []] },
            _config,
            app => _running.Contains(app.Name));

    private static ActivityApp App(string name, AppAction action) => new() { Name = name, Action = action };

    private static string[] Describe(ActivityPlan plan) =>
        plan.Steps.Select(step => step.Kind == ActivityStepKind.Wait ? "Wait" : $"{step.Kind} {step.Name}").ToArray();

    [Fact]
    public void Restart_stops_waits_then_starts()
    {
        var plan = Plan([App("A", AppAction.Restart)]);

        Assert.Equal(["StopApp A", "Wait", "StartApp A"], Describe(plan));
        Assert.Same(_config.Apps[0], plan.Steps[0].App);
    }

    [Fact]
    public void Start_skips_an_app_that_is_running()
    {
        _running.Add("A");

        var plan = Plan([App("A", AppAction.Start)]);

        Assert.Empty(plan.Steps);
        Assert.Equal(["A is already running.", "Nothing to do."], plan.Notes);
    }

    [Fact]
    public void Start_skips_a_running_window_app()
    {
        _running.Add("P");

        var plan = Plan([App("P", AppAction.Start)]);

        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void Start_runs_an_app_that_is_not_running()
    {
        Assert.Equal(["StartApp B"], Describe(Plan([App("B", AppAction.Start)])));
    }

    [Fact]
    public void Order_is_stops_closes_wait_starts_opens()
    {
        var plan = Plan(
            [App("B", AppAction.Start), App("A", AppAction.Stop)],
            [new ActivityWorkspace { Uri = U1 }, new ActivityWorkspace { Uri = U2, Action = WorkspaceAction.Close }]);

        Assert.Equal(["StopApp A", "CloseWorkspace two", "Wait", "StartApp B", "OpenWorkspace One (pinned)"], Describe(plan));
        Assert.Equal(U2, plan.Steps[1].Uri);
        Assert.Equal(U1, plan.Steps[4].Uri);
    }

    [Fact]
    public void Close_only_has_no_wait()
    {
        var plan = Plan(workspaces: [new ActivityWorkspace { Uri = U2, Action = WorkspaceAction.Close }]);

        Assert.Equal(["CloseWorkspace two"], Describe(plan));
    }

    [Fact]
    public void Unknown_and_disabled_apps_are_noted_and_the_rest_runs()
    {
        var plan = Plan([App("Gone", AppAction.Start), App("D", AppAction.Start), App("B", AppAction.Start)]);

        Assert.Equal(["StartApp B"], Describe(plan));
        Assert.Equal(["No app named 'Gone'; skipped.", "D is disabled; skipped."], plan.Notes);
    }

    [Fact]
    public void App_names_match_case_insensitively()
    {
        Assert.Equal(["StartApp B"], Describe(Plan([App("b", AppAction.Start)])));
    }

    [Fact]
    public void Stop_on_StopMethod_None_is_noted()
    {
        var plan = Plan([App("N", AppAction.Stop)]);

        Assert.Empty(plan.Steps);
        Assert.Equal(["N cannot be stopped (StopMethod None); skipped.", "Nothing to do."], plan.Notes);
    }

    [Fact]
    public void Restart_on_StopMethod_None_starts_if_not_running()
    {
        var plan = Plan([App("N", AppAction.Restart)]);

        Assert.Equal(["StartApp N"], Describe(plan));
        Assert.Equal(["N cannot be stopped (StopMethod None); starting it if not running."], plan.Notes);

        _running.Add("N");
        Assert.Empty(Plan([App("N", AppAction.Restart)]).Steps);
    }

    [Fact]
    public void Workspace_name_and_title_come_from_the_favourite()
    {
        var step = Assert.Single(Plan(workspaces: [new ActivityWorkspace { Uri = "FILE:///c%3A/work/one", Action = WorkspaceAction.Close }]).Steps);

        Assert.Equal("One (pinned)", step.Name);
        Assert.Equal("one*", step.WindowTitle);
    }

    [Fact]
    public void Workspace_not_in_favourites_uses_the_uri_name()
    {
        var step = Assert.Single(Plan(workspaces: [new ActivityWorkspace { Uri = U2 }]).Steps);

        Assert.Equal("two", step.Name);
        Assert.Null(step.WindowTitle);
    }

    [Fact]
    public void Empty_activity_has_nothing_to_do()
    {
        var plan = Plan();

        Assert.Empty(plan.Steps);
        Assert.Equal(["Nothing to do."], plan.Notes);
    }
}
