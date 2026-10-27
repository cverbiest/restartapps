namespace DevWorkspaceLauncher.Tests;

public class ActivityMigrationTests
{
    [Fact]
    public void Nothing_to_convert_without_profiles_or_sessions()
    {
        var config = new ConfigurationFile { Activities = [new Activity { Name = "Deploy" }] };

        Assert.False(ActivityMigration.IsNeeded(config));
        Assert.Empty(ActivityMigration.Convert(config));
        Assert.Single(config.Activities);
    }

    [Fact]
    public void A_profile_becomes_an_activity_with_the_same_ticks()
    {
        var config = new ConfigurationFile
        {
            Profiles =
            [
                new Profile
                {
                    Name = "Meeting",
                    Favorite = true,
                    Entries =
                    [
                        new ProfileEntry { Name = "Teams", Kill = true, Restart = true },
                        new ProfileEntry { Name = "Outlook", Kill = true },
                        new ProfileEntry { Name = "Spotify", Restart = true },
                        new ProfileEntry { Name = "Idle" }
                    ]
                }
            ]
        };

        var messages = ActivityMigration.Convert(config);

        Assert.Empty(config.Profiles);
        var activity = Assert.Single(config.Activities);
        Assert.Equal("Meeting", activity.Name);
        Assert.True(activity.Favorite);
        Assert.Equal(
            [("Teams", AppAction.Restart), ("Outlook", AppAction.Stop), ("Spotify", AppAction.Start)],
            activity.Apps.Select(app => (app.Name, app.Action)));
        Assert.Contains(messages, message => message.Contains("Meeting"));
    }

    [Fact]
    public void A_workspace_session_becomes_an_activity_that_opens_its_workspaces()
    {
        var config = new ConfigurationFile
        {
            WorkspaceProfiles = [new WorkspaceProfile { Name = "Develop", Workspaces = ["file:///c%3A/a", "file:///c%3A/b"] }]
        };

        ActivityMigration.Convert(config);

        Assert.Empty(config.WorkspaceProfiles);
        var activity = Assert.Single(config.Activities);
        Assert.Equal("Develop", activity.Name);
        Assert.Equal(
            [("file:///c%3A/a", WorkspaceAction.Open), ("file:///c%3A/b", WorkspaceAction.Open)],
            activity.Workspaces.Select(workspace => (workspace.Uri, workspace.Action)));
    }

    [Fact]
    public void A_name_already_taken_gets_a_suffix()
    {
        var config = new ConfigurationFile
        {
            Activities = [new Activity { Name = "Support" }],
            Profiles = [new Profile { Name = "support" }],
            WorkspaceProfiles = [new WorkspaceProfile { Name = "Support" }]
        };

        ActivityMigration.Convert(config);

        Assert.Equal(["Support", "support (profile)", "Support (session)"], config.Activities.Select(activity => activity.Name));
    }
}
