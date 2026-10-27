namespace DevWorkspaceLauncher.Tests;

public class ActivityOperationsTests
{
    [Fact]
    public void Adds_each_kind_with_its_default_action_once()
    {
        var activity = new Activity { Name = "X" };

        Assert.True(ActivityOperations.AddApp(activity, "Teams"));
        Assert.False(ActivityOperations.AddApp(activity, "teams"));
        Assert.True(ActivityOperations.AddWorkspace(activity, "file:///c%3A/a"));
        Assert.False(ActivityOperations.AddWorkspace(activity, "FILE:///c%3A/a"));
        Assert.True(ActivityOperations.AddRdpSession(activity, "demo"));
        Assert.False(ActivityOperations.AddRdpSession(activity, "Demo"));

        Assert.Equal(AppAction.Start, Assert.Single(activity.Apps).Action);
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
                new Activity { Name = "X", Apps = [new ActivityApp { Name = "Teams" }], RdpSessions = [new ActivityRdpSession { Name = "demo" }] }
            ]
        };

        ActivityOperations.RenameApp(config, "teams", "Teams (new)");
        ActivityOperations.RenameRdpSession(config, "DEMO", "demo25");

        Assert.Equal("Teams (new)", config.Activities[0].Apps[0].Name);
        Assert.Equal("demo25", config.Activities[0].RdpSessions[0].Name);
    }

    [Fact]
    public void Removing_an_app_or_session_takes_it_out_of_activities()
    {
        var config = new ConfigurationFile
        {
            Activities =
            [
                new Activity { Name = "X", Apps = [new ActivityApp { Name = "Teams" }], RdpSessions = [new ActivityRdpSession { Name = "demo" }] }
            ]
        };

        ActivityOperations.RemoveApp(config, "TEAMS");
        ActivityOperations.RemoveRdpSession(config, "demo");

        Assert.Empty(config.Activities[0].Apps);
        Assert.Empty(config.Activities[0].RdpSessions);
    }
}
