namespace DevWorkspaceLauncher.Tests;

public class WindowLayoutTests
{
    private static readonly MonitorInfo Laptop = new(new(0, 0, 1920, 1200), new(0, 0, 1920, 1152), Primary: true, 144);
    private static readonly MonitorInfo Left27 = new(new(-2560, -240, 0, 1200), new(-2560, -240, 0, 1152), Primary: false, 96);
    private static readonly MonitorInfo Right27 = new(new(1920, -240, 4480, 1200), new(1920, -240, 4480, 1152), Primary: false, 96);

    private static WindowIdentity Code(string title) => new("Code.exe", title);

    [Fact]
    public void The_same_monitors_in_another_order_give_the_same_key()
    {
        Assert.Equal(MonitorSetup.KeyOf([Laptop, Left27, Right27]), MonitorSetup.KeyOf([Right27, Laptop, Left27]));
    }

    [Fact]
    public void Another_primary_monitor_or_arrangement_gives_another_key()
    {
        var otherPrimary = new[] { Laptop with { Primary = false }, Left27 with { Primary = true } };
        Assert.NotEqual(MonitorSetup.KeyOf([Laptop, Left27]), MonitorSetup.KeyOf(otherPrimary));
        Assert.NotEqual(MonitorSetup.KeyOf([Laptop, Left27]), MonitorSetup.KeyOf([Laptop, Right27]));
    }

    [Fact]
    public void Monitors_are_described_left_to_right_with_the_main_one_marked()
    {
        Assert.Equal("3 monitors: 2560×1440, 1920×1200 (main), 2560×1440", MonitorSetup.Describe([Laptop, Right27, Left27]));
        Assert.Equal("1 monitor: 1920×1200", MonitorSetup.Describe([Laptop]));
    }

    [Fact]
    public void A_window_with_the_same_program_and_title_is_matched_first()
    {
        var recorded = new[] { Code("a - Visual Studio Code"), Code("b - Visual Studio Code") };
        var open = new[] { Code("b - Visual Studio Code"), Code("a - Visual Studio Code") };

        Assert.Equal([(0, 1), (1, 0)], WindowMatcher.Match(recorded, open));
    }

    [Fact]
    public void Otherwise_the_windows_of_a_program_are_matched_in_order()
    {
        var recorded = new[] { new WindowIdentity("brave.exe", "Inbox"), Code("a"), new WindowIdentity("brave.exe", "Docs") };
        var open = new[] { new WindowIdentity("BRAVE.EXE", "News"), new WindowIdentity("brave.exe", "Docs"), new WindowIdentity("brave.exe", "Mail") };

        // "Docs" pairs by title; "Inbox" takes the first brave window left; Code is not open.
        Assert.Equal([(0, 0), (2, 1)], WindowMatcher.Match(recorded, open));
    }

    [Fact]
    public void Each_open_window_is_used_once()
    {
        var recorded = new[] { Code("a"), Code("b"), Code("c") };
        var open = new[] { Code("x") };

        Assert.Single(WindowMatcher.Match(recorded, open));
    }

    [Fact]
    public void Recording_again_for_the_same_monitors_replaces_the_layout()
    {
        var key = MonitorSetup.KeyOf([Laptop]);
        var layouts = new List<WindowLayout>
        {
            new() { Monitors = key, Description = "old" },
            new() { Monitors = MonitorSetup.KeyOf([Laptop, Left27]), Description = "docked" }
        };

        WindowLayoutStore.Put(layouts, new WindowLayout { Monitors = key, Description = "new" });

        Assert.Equal(["new", "docked"], layouts.Select(layout => layout.Description));
    }
}
