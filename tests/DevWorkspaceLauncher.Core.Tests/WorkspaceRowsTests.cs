namespace DevWorkspaceLauncher.Tests;

public class WorkspaceRowsTests
{
    private static VsCodeWorkspace Workspace(string id, string uri, string name, string location, int day)
    {
        return new VsCodeWorkspace(
            id,
            uri,
            WorkspaceKind.Folder,
            name,
            location,
            new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
    }

    private static readonly VsCodeWorkspace Alpha = Workspace("a", "file:///c%3A/code/alpha", "alpha", @"c:\code\alpha", 3);
    private static readonly VsCodeWorkspace Beta = Workspace("b", "file:///c%3A/code/beta", "beta", @"c:\code\beta", 2);
    private static readonly VsCodeWorkspace Gamma = Workspace("g", "file:///c%3A/code/gamma", "gamma", @"c:\code\gamma", 1);

    [Fact]
    public void Preferred_view_lists_favourites_in_configuration_order()
    {
        List<WorkspaceFavorite> favorites =
        [
            new() { Name = "gamma", Uri = Gamma.Uri },
            new() { Name = "alpha", Uri = Alpha.Uri }
        ];

        var rows = WorkspaceRows.Build([Alpha, Beta, Gamma], favorites, showAll: false);

        Assert.Equal(["gamma", "alpha"], rows.Select(row => row.Name));
        Assert.All(rows, row => Assert.True(row.Preferred));
        Assert.Equal("g", rows[0].Id);
        Assert.Equal(Gamma.LastUsed, rows[0].LastUsed);
    }

    [Fact]
    public void Preferred_view_keeps_a_favourite_vs_code_has_forgotten()
    {
        List<WorkspaceFavorite> favorites =
        [
            new() { Name = "delta", Uri = "file:///c%3A/code/delta/delta.code-workspace" }
        ];

        var row = Assert.Single(WorkspaceRows.Build([Alpha], favorites, showAll: false));

        Assert.Equal("delta", row.Name);
        Assert.Null(row.Id);
        Assert.Null(row.LastUsed);
        Assert.Equal(WorkspaceKind.WorkspaceFile, row.Kind);
        Assert.Equal(@"c:\code\delta\delta.code-workspace", row.Location);
        Assert.True(row.Preferred);
    }

    [Fact]
    public void Preferred_view_falls_back_to_the_workspace_name_when_the_favourite_has_none()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "   ", Uri = "file:///c%3A/code/delta" }];

        var row = Assert.Single(WorkspaceRows.Build([], favorites, showAll: false));

        Assert.Equal("delta", row.Name);
    }

    [Fact]
    public void All_view_lists_every_workspace_most_recent_first()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "beta (mine)", Uri = Beta.Uri }];

        var rows = WorkspaceRows.Build([Alpha, Beta, Gamma], favorites, showAll: true);

        Assert.Equal(["alpha", "beta (mine)", "gamma"], rows.Select(row => row.Name));
        Assert.Equal([false, true, false], rows.Select(row => row.Preferred));
        Assert.Equal([Alpha.LastUsed, Beta.LastUsed, Gamma.LastUsed], rows.Select(row => row.LastUsed));
    }

    [Fact]
    public void All_view_still_shows_a_favourite_vs_code_has_forgotten_first()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "delta", Uri = "file:///c%3A/code/delta" }];

        var rows = WorkspaceRows.Build([Alpha], favorites, showAll: true);

        Assert.Equal(["delta", "alpha"], rows.Select(row => row.Name));
        Assert.True(rows[0].Preferred);
    }

    [Fact]
    public void Matches_favourites_to_workspaces_regardless_of_case()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "alpha", Uri = Alpha.Uri.ToUpperInvariant() }];

        var row = Assert.Single(WorkspaceRows.Build([Alpha], favorites, showAll: true));

        Assert.True(row.Preferred);
        Assert.Equal("a", row.Id);
    }
}
