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

    private bool _showHidden;
    private string? _networksSignature;

    private static AppState State => App.State;

    public StartPage()
    {
        InitializeComponent();
        Tiles.ItemsSource = _tiles;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        NetworkStatus.Changed += Network_Changed;
        Refresh();
        RefreshNetworks();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
        NetworkStatus.Changed -= Network_Changed;
    }

    private void State_Changed(object? sender, EventArgs e)
    {
        Refresh();
        RefreshNetworks();
    }

    private void Network_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshNetworks);

    /// <summary>Reads the interfaces off the UI thread, then shows them; unchanged ones are left alone.</summary>
    private async void RefreshNetworks()
    {
        var settings = State.Config.NetworkInterfaces.ToList();
        var interfaces = await Task.Run(() => NetworkStatus.Current(settings));
        var all = interfaces.Select(status => new NetworkItem(status, NetworkInterfaceSettings.Find(State.Config, status.Id))).ToList();
        var shown = all.Where(item => _showHidden || item.Settings?.Hidden != true).ToList();

        var signature = string.Join("\n", shown.Select(item => item.Signature));
        if (signature != _networksSignature)
        {
            _networksSignature = signature;
            Networks.ItemsSource = shown;
        }

        NoNetwork.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var hidden = all.Count(item => item.Settings?.Hidden == true);
        ShowHidden.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowHidden.Content = _showHidden ? "Leave out hidden interfaces" : $"Show {hidden} hidden interface{(hidden == 1 ? "" : "s")}";
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        _showHidden = !_showHidden;
        RefreshNetworks();
    }

    private void NetworkCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NetworkItem item, Tag: string what })
        {
            Clipboard.Copy(what switch
            {
                "address" => item.Status.PrimaryAddress ?? string.Empty,
                "name" => item.Name,
                _ => item.Status.DetailsText(item.Settings)
            });
        }
    }

    private void NetworkConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NetworkItem item })
        {
            NetworkStatus.Connect(State, item.Status, item.Settings?.Connect);
        }
    }

    /// <summary>Opens the interface's details window.</summary>
    private void Network_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NetworkItem item })
        {
            NetworkDetailsWindow.Show(State, item);
        }
    }

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
