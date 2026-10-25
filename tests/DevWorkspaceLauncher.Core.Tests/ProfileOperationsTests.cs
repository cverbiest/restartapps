namespace DevWorkspaceLauncher.Tests;

public class ProfileOperationsTests
{
    private static AppConfiguration App(string name, StopMethod stopMethod = StopMethod.Process) =>
        new() { Name = name, Commandline = name + ".exe", Kill = true, Restart = true, StopMethod = stopMethod };

    [Fact]
    public void Apply_sets_ticks_and_clears_apps_the_profile_does_not_list()
    {
        var listed = App("Listed");
        var unlisted = App("Unlisted");
        var profile = new Profile
        {
            Name = "P",
            Entries = [new ProfileEntry { Name = "listed", Kill = false, Restart = true }]
        };

        var cleared = ProfileOperations.Apply(profile, [listed, unlisted]);

        Assert.Equal(1, cleared);
        Assert.False(listed.Kill);
        Assert.True(listed.Restart);
        Assert.False(unlisted.Kill);
        Assert.False(unlisted.Restart);
    }

    [Fact]
    public void Apply_never_ticks_Kill_on_an_app_that_cannot_stop()
    {
        var app = App("Teams", StopMethod.None);
        var profile = new Profile { Name = "P", Entries = [new ProfileEntry { Name = "Teams", Kill = true, Restart = true }] };

        ProfileOperations.Apply(profile, [app]);

        Assert.False(app.Kill);
        Assert.True(app.Restart);
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        var profiles = new List<Profile> { new() { Name = "Docking" } };

        Assert.Same(profiles[0], ProfileOperations.Find(profiles, "docking"));
        Assert.Null(ProfileOperations.Find(profiles, "other"));
        Assert.Null(ProfileOperations.Find(profiles, null));
    }

    [Fact]
    public void Upsert_overwrites_by_name_case_insensitively()
    {
        var profiles = new List<Profile> { new() { Name = "Docking", Entries = [new ProfileEntry { Name = "Old" }] } };

        var result = ProfileOperations.Upsert(profiles, "DOCKING", [new ProfileEntry { Name = "New" }]);

        Assert.Single(profiles);
        Assert.Same(profiles[0], result);
        Assert.Equal("Docking", result.Name);
        Assert.Equal("New", Assert.Single(result.Entries).Name);
    }

    [Fact]
    public void Upsert_adds_a_new_profile()
    {
        var profiles = new List<Profile>();

        var result = ProfileOperations.Upsert(profiles, "VPN", []);

        Assert.Same(result, Assert.Single(profiles));
        Assert.Equal("VPN", result.Name);
    }

    [Fact]
    public void Capture_copies_name_and_ticks()
    {
        var app = App("A");
        app.Kill = false;

        var entry = Assert.Single(ProfileOperations.Capture([app]));

        Assert.Equal("A", entry.Name);
        Assert.False(entry.Kill);
        Assert.True(entry.Restart);
    }
}
