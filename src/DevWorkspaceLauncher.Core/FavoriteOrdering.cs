namespace DevWorkspaceLauncher;

/// <summary>Something that can be pinned to the start page and the tray menu, in an order of the user's choosing.</summary>
public interface IFavorite
{
    bool Favorite { get; set; }

    /// <summary>The position on the start page and in the tray menu, from 1; 0 means not placed yet (last).</summary>
    int FavoriteOrder { get; set; }
}

/// <summary>The order of the favorites: by <see cref="IFavorite.FavoriteOrder"/>, then new pins in their listed order.</summary>
public static class FavoriteOrdering
{
    /// <summary>The favorites among <paramref name="items"/>, in their start page order.</summary>
    public static List<T> Order<T>(IEnumerable<T> items, Func<T, IFavorite> favorite) =>
        items
            .Where(item => favorite(item).Favorite)
            .OrderBy(item => favorite(item).FavoriteOrder is var order and > 0 ? order : int.MaxValue)
            .ToList();

    /// <summary>Stores the order of <paramref name="ordered"/> as positions 1, 2, 3...</summary>
    public static void Renumber(IEnumerable<IFavorite> ordered)
    {
        var position = 1;
        foreach (var item in ordered)
        {
            item.FavoriteOrder = position++;
        }
    }

    /// <summary>Pins (it goes last) or unpins; an unpinned item forgets its position.</summary>
    public static void Toggle(IFavorite item)
    {
        item.Favorite = !item.Favorite;
        item.FavoriteOrder = 0;
    }
}
