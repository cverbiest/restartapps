namespace DevWorkspaceLauncher.Tests;

public class FavoriteOrderingTests
{
    private static Activity A(string name, bool favorite = true, int order = 0) =>
        new() { Name = name, Favorite = favorite, FavoriteOrder = order };

    [Fact]
    public void Orders_by_position_and_puts_new_pins_last_in_their_listed_order()
    {
        Activity[] items = [A("new1"), A("second", order: 2), A("off", favorite: false), A("first", order: 1), A("new2")];

        var ordered = FavoriteOrdering.Order(items, item => item);

        Assert.Equal(["first", "second", "new1", "new2"], ordered.Select(item => item.Name));
    }

    [Fact]
    public void Renumber_stores_the_given_order()
    {
        Activity[] items = [A("c", order: 7), A("a"), A("b", order: 1)];

        FavoriteOrdering.Renumber(items);

        Assert.Equal([1, 2, 3], items.Select(item => item.FavoriteOrder));
    }

    [Fact]
    public void Toggle_pins_last_and_unpinning_forgets_the_position()
    {
        var item = A("x", favorite: false);

        FavoriteOrdering.Toggle(item);
        Assert.True(item.Favorite);

        item.FavoriteOrder = 4;
        FavoriteOrdering.Toggle(item);
        Assert.False(item.Favorite);
        Assert.Equal(0, item.FavoriteOrder);
    }

    [Fact]
    public void Favorite_order_round_trips_and_is_left_out_when_not_set()
    {
        var config = new ConfigurationFile
        {
            Apps = [new AppConfiguration { Name = "Teams", Commandline = "teams.exe", Favorite = true, FavoriteOrder = 2 }],
            RdpSessions = [new RdpSession { Name = "demo", Host = "demo25", Favorite = true }]
        };

        var json = AppConfigurationStore.ToJson(config);
        var reloaded = AppConfigurationStore.Parse(json);

        Assert.Equal(2, reloaded.Apps[0].FavoriteOrder);
        Assert.Equal(1, json.Split("FavoriteOrder").Length - 1);
    }
}
