using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// The favorites, one click each, in the order of the tray menu. Dragging a tile or its right-click
/// menu changes that order, which is saved with the items (<see cref="IFavorite.FavoriteOrder"/>).
/// </summary>
public sealed partial class StartPage : Page
{
    // An ObservableCollection, so the GridView can reorder it by drag and drop.
    private readonly ObservableCollection<LaunchItem> _tiles = [];

    private static AppState State => App.State;

    public StartPage()
    {
        InitializeComponent();
        Tiles.ItemsSource = _tiles;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
    }

    private void State_Changed(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _tiles.Clear();
        foreach (var item in LaunchItem.Favorites(State.Config))
        {
            _tiles.Add(item);
        }

        Empty.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tiles_ItemClick(object sender, ItemClickEventArgs e) => ((LaunchItem)e.ClickedItem).Run();

    /// <summary>The GridView has already moved the tile in the collection; store that order.</summary>
    private void Tiles_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => SaveOrder();

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LaunchItem item, Tag: string where })
        {
            return;
        }

        var from = _tiles.IndexOf(item);
        var to = where switch
        {
            "first" => 0,
            "earlier" => Math.Max(0, from - 1),
            "later" => Math.Min(_tiles.Count - 1, from + 1),
            _ => _tiles.Count - 1
        };

        if (from >= 0 && from != to)
        {
            _tiles.Move(from, to);
            SaveOrder();
        }
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LaunchItem item })
        {
            FavoriteOrdering.Toggle(item.Target);
            SaveOrder();
        }
    }

    private void SaveOrder()
    {
        FavoriteOrdering.Renumber(_tiles.Select(tile => tile.Target).Where(target => target.Favorite));
        State.Save();
    }
}
