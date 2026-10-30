namespace DevWorkspaceLauncher.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Reads_a_file_that_predates_workspaces()
    {
        var json = """
        {
          "Apps": [ { "Name": "RDP Plus", "Commandline": "rdp.exe", "Kill": true, "Restart": true } ],
          "Profiles": []
        }
        """;

        var configuration = AppConfigurationStore.Parse(json);

        Assert.Single(configuration.Apps);
        Assert.Empty(configuration.Workspaces);
        Assert.Null(configuration.VsCodeExecutable);
    }

    [Fact]
    public void Reads_the_original_bare_array_layout()
    {
        var configuration = AppConfigurationStore.Parse("""[ { "Name": "RDP Plus", "Commandline": "rdp.exe" } ]""");

        Assert.Single(configuration.Apps);
        Assert.Empty(configuration.Workspaces);
    }

    [Fact]
    public void Reads_workspaces_and_the_executable_override()
    {
        var json = """
        {
          "Apps": [],
          "Profiles": [],
          "Workspaces": [ { "Name": "develop (linux)", "Uri": "vscode-remote://ssh-remote%2Bhost/home/develop.code-workspace" } ],
          "VsCodeExecutable": "C:\\Program Files\\Microsoft VS Code\\Code.exe"
        }
        """;

        var configuration = AppConfigurationStore.Parse(json);

        var workspace = Assert.Single(configuration.Workspaces);
        Assert.Equal("develop (linux)", workspace.Name);
        Assert.Equal("vscode-remote://ssh-remote%2Bhost/home/develop.code-workspace", workspace.Uri);
        Assert.Equal(@"C:\Program Files\Microsoft VS Code\Code.exe", configuration.VsCodeExecutable);
    }

    [Fact]
    public void Round_trips_workspaces_through_a_save()
    {
        var configuration = new ConfigurationFile
        {
            Workspaces = [new WorkspaceFavorite { Name = "restartapps", Uri = "file:///c%3A/code/restartapps" }]
        };

        var reloaded = AppConfigurationStore.Parse(AppConfigurationStore.ToJson(configuration));

        var workspace = Assert.Single(reloaded.Workspaces);
        Assert.Equal("restartapps", workspace.Name);
        Assert.Equal("file:///c%3A/code/restartapps", workspace.Uri);
    }

    [Fact]
    public void Reads_workspace_sessions_and_a_window_title_pattern()
    {
        var json = """
        {
          "Apps": [],
          "Workspaces": [
            { "Name": "develop (linux)", "Uri": "vscode-remote://ssh-remote%2Bhost/develop", "WindowTitle": "develop linux*" }
          ],
          "WorkspaceProfiles": [
            { "Name": "Linux dev", "Workspaces": [ "vscode-remote://ssh-remote%2Bhost/develop", "file:///c%3A/code/alpha" ] }
          ]
        }
        """;

        var configuration = AppConfigurationStore.Parse(json);

        Assert.Equal("develop linux*", Assert.Single(configuration.Workspaces).WindowTitle);

        var session = Assert.Single(configuration.WorkspaceProfiles);
        Assert.Equal("Linux dev", session.Name);
        Assert.Equal(2, session.Workspaces.Count);
        Assert.Equal("file:///c%3A/code/alpha", session.Workspaces[1]);
    }

    [Fact]
    public void Round_trips_a_workspace_session_through_a_save()
    {
        var configuration = new ConfigurationFile
        {
            WorkspaceProfiles = [new WorkspaceProfile { Name = "Linux dev", Workspaces = ["file:///c%3A/code/alpha"] }]
        };

        var reloaded = AppConfigurationStore.Parse(AppConfigurationStore.ToJson(configuration));

        var session = Assert.Single(reloaded.WorkspaceProfiles);
        Assert.Equal("Linux dev", session.Name);
        Assert.Equal(["file:///c%3A/code/alpha"], session.Workspaces);
    }

    [Fact]
    public void Omits_a_window_title_pattern_that_is_not_set()
    {
        var configuration = new ConfigurationFile
        {
            Workspaces = [new WorkspaceFavorite { Name = "alpha", Uri = "file:///c%3A/code/alpha" }]
        };

        Assert.DoesNotContain("WindowTitle", AppConfigurationStore.ToJson(configuration));
    }

    [Fact]
    public void Round_trips_favorites_through_a_save()
    {
        var configuration = new ConfigurationFile
        {
            Profiles = [new Profile { Name = "Work", Favorite = true }],
            WorkspaceProfiles = [new WorkspaceProfile { Name = "Linux dev", Favorite = true }],
            Activities = [new Activity { Name = "Deploy", Favorite = true }],
            RdpSessions = [new RdpSession { Name = "demo", Host = "demo25", Favorite = true }]
        };

        var reloaded = AppConfigurationStore.Parse(AppConfigurationStore.ToJson(configuration));

        Assert.True(Assert.Single(reloaded.Profiles).Favorite);
        Assert.True(Assert.Single(reloaded.WorkspaceProfiles).Favorite);
        Assert.True(Assert.Single(reloaded.Activities).Favorite);
        Assert.True(Assert.Single(reloaded.RdpSessions).Favorite);
    }

    [Fact]
    public void Omits_favorite_when_it_is_not_set()
    {
        var configuration = new ConfigurationFile
        {
            Profiles = [new Profile { Name = "Work" }],
            RdpSessions = [new RdpSession { Name = "demo", Host = "demo25" }]
        };

        Assert.DoesNotContain("Favorite", AppConfigurationStore.ToJson(configuration));
    }

    [Fact]
    public void Omits_the_executable_override_when_it_is_not_set()
    {
        var json = AppConfigurationStore.ToJson(new ConfigurationFile());

        Assert.DoesNotContain("VsCodeExecutable", json);
        Assert.Contains("Workspaces", json);
    }
}
