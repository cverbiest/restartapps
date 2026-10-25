namespace DevWorkspaceLauncher.Tests;

public class SessionOperationsTests
{
    [Fact]
    public void ApplyToTicks_opens_listed_and_clears_others_but_keeps_close()
    {
        var ticks = new Dictionary<string, (bool Close, bool Open)>(StringComparer.OrdinalIgnoreCase)
        {
            ["file:///unlisted"] = (true, true)
        };
        var session = new WorkspaceProfile { Name = "S", Workspaces = ["file:///listed"] };

        SessionOperations.ApplyToTicks(session, ticks);

        Assert.Equal((true, false), ticks["file:///unlisted"]);
        Assert.Equal((false, true), ticks["file:///listed"]);
    }

    [Fact]
    public void ResolveNames_prefers_row_names_and_falls_back_to_the_uri()
    {
        var session = new WorkspaceProfile
        {
            Name = "S",
            Workspaces = ["file:///c%3A/work/known", "file:///c%3A/work/unknown"]
        };
        var rows = new[]
        {
            new WorkspaceRow { Uri = "file:///c%3A/work/known", Kind = WorkspaceKind.Folder, Name = "Renamed", Location = @"c:\work\known" }
        };

        var resolved = SessionOperations.ResolveNames(session, rows);

        Assert.Equal(
            [("file:///c%3A/work/known", "Renamed"), ("file:///c%3A/work/unknown", "unknown")],
            resolved);
    }

    [Fact]
    public void Session_Upsert_overwrites_case_insensitively()
    {
        var sessions = new List<WorkspaceProfile> { new() { Name = "Linux dev", Workspaces = ["a"] } };

        var result = SessionOperations.Upsert(sessions, "linux DEV", ["b"]);

        Assert.Same(sessions[0], Assert.Single(sessions));
        Assert.Equal(["b"], result.Workspaces);
        Assert.Same(sessions[0], SessionOperations.Find(sessions, "LINUX dev"));
    }
}
