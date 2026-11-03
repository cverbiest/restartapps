namespace DevWorkspaceLauncher.Tests;

public class WindowPlacementTests
{
    private static readonly MonitorInfo Primary = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), Primary: true, 96);
    private static readonly MonitorInfo Left = new(new(-2560, 0, 0, 1440), new(-2560, 0, 0, 1400), Primary: false, 96);

    [Fact]
    public void A_window_on_a_connected_monitor_stays_where_it_was()
    {
        var placement = new WindowPlacement(-2000, 100, 1100, 780, Maximized: true);

        Assert.Equal(placement, placement.Fit([Primary, Left]));
    }

    [Fact]
    public void A_window_on_a_monitor_that_is_gone_is_centred_on_the_primary_monitor()
    {
        var placement = new WindowPlacement(-2000, 100, 1100, 780, Maximized: true);

        Assert.Equal(new WindowPlacement(410, 130, 1100, 780, Maximized: true), placement.Fit([Primary]));
    }

    [Fact]
    public void A_window_larger_than_the_primary_monitor_is_shrunk_to_its_working_area()
    {
        var placement = new WindowPlacement(-2500, 0, 2400, 1300);

        Assert.Equal(new WindowPlacement(0, 0, 1920, 1040), placement.Fit([Primary]));
    }

    [Theory]
    [InlineData(1850, 100)] // only 70 pixels of the title bar on screen
    [InlineData(100, 1030)] // title bar below the working area, behind the taskbar
    [InlineData(100, -20)] // title bar above the top of the screen
    public void A_window_whose_title_bar_cannot_be_dragged_is_moved(int left, int top)
    {
        var placement = new WindowPlacement(left, top, 1100, 780);

        Assert.NotEqual(placement, placement.Fit([Primary]));
    }
}
