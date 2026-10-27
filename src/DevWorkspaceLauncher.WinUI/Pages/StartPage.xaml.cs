using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>The favorites, one click each - the same items as the tray menu.</summary>
public sealed partial class StartPage : Page
{
    private static AppState State => App.State;

    public StartPage()
    {
        InitializeComponent();
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
        var favorites = LaunchItem.Favorites(State.Config).ToList();
        Tiles.ItemsSource = favorites;
        Empty.Visibility = favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tiles_ItemClick(object sender, ItemClickEventArgs e) => ((LaunchItem)e.ClickedItem).Run();
}
