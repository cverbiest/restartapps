using System.Text.Json;

namespace DevWorkspaceLauncher.Tests;

public class ActivityConfigurationTests
{
    [Fact]
    public void Activities_parse_with_lowercase_actions_and_defaults()
    {
        var configuration = AppConfigurationStore.Parse("""
            { "Activities": [
                { "Name": "X",
                  "Apps": [ { "Name": "A", "action": "restart" }, { "Name": "B" } ],
                  "Workspaces": [ { "Uri": "file:///c%3A/x" }, { "Uri": "file:///c%3A/y", "Action": "close" } ] },
                { "Name": "Empty" } ] }
            """);

        var x = configuration.Activities[0];
        Assert.Equal([AppAction.Restart, AppAction.Start], x.Apps.Select(app => app.Action));
        Assert.Equal([WorkspaceAction.Open, WorkspaceAction.Close], x.Workspaces.Select(workspace => workspace.Action));

        var empty = configuration.Activities[1];
        Assert.Empty(empty.Apps);
        Assert.Empty(empty.Workspaces);
    }

    [Fact]
    public void Null_item_lists_are_empty_lists()
    {
        var activity = Assert.Single(AppConfigurationStore.Parse("""
            { "Activities": [ { "Name": "X", "Apps": null, "Workspaces": null } ] }
            """).Activities);

        Assert.Empty(activity.Apps);
        Assert.Empty(activity.Workspaces);
    }

    [Fact]
    public void Missing_Activities_is_an_empty_list()
    {
        Assert.Empty(AppConfigurationStore.Parse("""{ "Apps": [] }""").Activities);
    }

    [Fact]
    public void Unknown_action_reports_the_valid_values()
    {
        var error = Assert.Throws<JsonException>(() => AppConfigurationStore.Parse("""
            { "Activities": [ { "Name": "X", "Apps": [ { "Name": "A", "Action": "Reboot" } ] } ] }
            """));

        Assert.Contains("Unknown Action 'Reboot'. Use Start, Stop or Restart.", error.Message);
    }

    [Fact]
    public void Unknown_StopMethod_still_reports_its_valid_values()
    {
        var error = Assert.Throws<JsonException>(() => AppConfigurationStore.Parse("""
            { "Apps": [ { "Name": "A", "Commandline": "a.exe", "StopMethod": "Nuke" } ] }
            """));

        Assert.Contains("Unknown StopMethod 'Nuke'. Use Process, Window or None.", error.Message);
    }

    [Fact]
    public void Activities_round_trip()
    {
        var configuration = new ConfigurationFile
        {
            Activities =
            [
                new Activity
                {
                    Name = "Docking",
                    Apps = [new ActivityApp { Name = "Teams", Action = AppAction.Restart }],
                    Workspaces = [new ActivityWorkspace { Uri = "file:///c%3A/x", Action = WorkspaceAction.Close }]
                }
            ]
        };

        var activity = Assert.Single(AppConfigurationStore.Parse(AppConfigurationStore.ToJson(configuration)).Activities);

        Assert.Equal("Docking", activity.Name);
        Assert.Equal(AppAction.Restart, Assert.Single(activity.Apps).Action);
        Assert.Equal(WorkspaceAction.Close, Assert.Single(activity.Workspaces).Action);
        Assert.Contains("\"Action\": \"Restart\"", AppConfigurationStore.ToJson(configuration));
    }
}
