namespace DevWorkspaceLauncher.Tests;

public class VsCodeWindowsTests
{
    private static readonly string[] Titles =
    [
        "restartapps - Untitled-1",
        "b1-cce-poc - Dev Container: Local Dev Container",
        "develop linux",
        "develop windows",
        "schema - SSH: cce-dev03_cvb - lisa_inc_add.df"
    ];

    [Fact]
    public void Finds_the_one_window_whose_title_carries_the_name()
    {
        var (match, index) = VsCodeWindows.Select(Titles, "restartapps", null);

        Assert.Equal(WindowMatch.Single, match);
        Assert.Equal(0, index);
    }

    [Fact]
    public void Refuses_to_choose_between_two_workspaces_of_the_same_name()
    {
        var (match, index) = VsCodeWindows.Select(Titles, "develop", null);

        Assert.Equal(WindowMatch.Ambiguous, match);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void An_explicit_pattern_settles_an_ambiguous_name()
    {
        var (match, index) = VsCodeWindows.Select(Titles, "develop", "develop linux*");

        Assert.Equal(WindowMatch.Single, match);
        Assert.Equal(2, index);
    }

    [Fact]
    public void A_pattern_wins_over_the_name()
    {
        var (match, index) = VsCodeWindows.Select(Titles, "restartapps", "schema*");

        Assert.Equal(WindowMatch.Single, match);
        Assert.Equal(4, index);
    }

    [Fact]
    public void Reports_no_match_when_the_window_is_not_there()
    {
        var (match, index) = VsCodeWindows.Select(Titles, "kendo", null);

        Assert.Equal(WindowMatch.None, match);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void Reports_no_match_when_a_pattern_matches_nothing()
    {
        var (match, _) = VsCodeWindows.Select(Titles, "develop", "develop macos*");

        Assert.Equal(WindowMatch.None, match);
    }

    [Fact]
    public void Matches_the_name_regardless_of_case()
    {
        var (match, index) = VsCodeWindows.Select(Titles, "RESTARTAPPS", null);

        Assert.Equal(WindowMatch.Single, match);
        Assert.Equal(0, index);
    }

    [Fact]
    public void Reports_no_match_when_nothing_is_open()
    {
        var (match, _) = VsCodeWindows.Select([], "restartapps", null);

        Assert.Equal(WindowMatch.None, match);
    }

    [Fact]
    public void Lists_every_candidate_so_an_ambiguous_case_can_be_explained()
    {
        Assert.Equal([2, 3], VsCodeWindows.MatchAll(Titles, "develop", null));
    }
}
