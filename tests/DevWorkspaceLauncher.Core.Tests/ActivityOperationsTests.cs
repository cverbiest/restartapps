namespace DevWorkspaceLauncher.Tests;

public class ActivityOperationsTests
{
    [Fact]
    public void Adds_each_kind_with_its_default_action_once()
    {
        var activity = new Activity { Name = "X" };

        Assert.True(ActivityOperations.AddAction(activity, new ApplicationAction { Name = "Teams", Commandline = "teams.exe" }));
        Assert.False(ActivityOperations.AddAction(activity, new ApplicationAction { Name = "teams", Commandline = "teams.exe" }));
        Assert.True(ActivityOperations.AddWorkspace(activity, "file:///c%3A/a"));
        Assert.False(ActivityOperations.AddWorkspace(activity, "FILE:///c%3A/a"));
        Assert.True(ActivityOperations.AddRdpSession(activity, "demo"));
        Assert.False(ActivityOperations.AddRdpSession(activity, "Demo"));

        Assert.Equal(ActionVerb.Start, Assert.Single(activity.Actions).Action);
        Assert.Equal(WorkspaceAction.Open, Assert.Single(activity.Workspaces).Action);
        Assert.Equal("demo", Assert.Single(activity.RdpSessions).Name);
    }

    [Fact]
    public void Renaming_an_app_or_session_follows_into_activities()
    {
        var config = new ConfigurationFile
        {
            Activities =
            [
                new Activity { Name = "X", Actions = [new ActivityAction { Name = "Teams" }], RdpSessions = [new ActivityRdpSession { Name = "demo" }] }
            ]
        };

        ActivityOperations.RenameAction(config, "teams", "Teams (new)");
        ActivityOperations.RenameRdpSession(config, "DEMO", "demo25");

        Assert.Equal("Teams (new)", config.Activities[0].Actions[0].Name);
        Assert.Equal("demo25", config.Activities[0].RdpSessions[0].Name);
    }

    [Fact]
    public void Removing_an_app_or_session_takes_it_out_of_activities()
    {
        var config = new ConfigurationFile
        {
            Activities =
            [
                new Activity { Name = "X", Actions = [new ActivityAction { Name = "Teams" }], RdpSessions = [new ActivityRdpSession { Name = "demo" }] }
            ]
        };

        ActivityOperations.RemoveAction(config, "TEAMS");
        ActivityOperations.RemoveRdpSession(config, "demo");

        Assert.Empty(config.Activities[0].Actions);
        Assert.Empty(config.Activities[0].RdpSessions);
    }
}
