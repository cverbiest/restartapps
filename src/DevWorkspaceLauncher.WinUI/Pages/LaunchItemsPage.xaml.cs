using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// A list of launchable items to run, pin, delete and add to activities: every kind (All items), or
/// only the activities - which can then also be created and edited - when navigated to with
/// <see cref="ActivitiesOnly"/>.
/// </summary>
public sealed partial class LaunchItemsPage : Page
{
    /// <summary>The navigation parameter that shows only the activities.</summary>
    public const string ActivitiesOnly = "activities";

    private readonly ColumnSort<LaunchItem> _sort;
    private bool _activitiesOnly;

    private static AppState State => App.State;

    public LaunchItemsPage()
    {
        InitializeComponent();
        ItemActions.AttachActivityMenu(ActivityMenu, () => XamlRoot, () => Selected().Select(item => item.Target).ToList());
        _sort = new ColumnSort<LaunchItem>(Refresh,
            (FavHeader, item => item.Favorite ? 0 : 1),
            (NameHeader, item => item.Name),
            (TypeHeader, item => item.Kind));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _activitiesOnly = e.Parameter as string == ActivitiesOnly;
        Heading.Text = _activitiesOnly ? "Activities" : "All items";
        Intro.Text = _activitiesOnly
            ? "An activity starts and stops apps, opens and closes workspaces and connects RDP sessions and SSH hosts in one click. Add items to one with Add to activity on any tab; Edit changes what it does with each. Pin an activity to put it on the start page and in the tray."
            : "Everything that runs with one click. Double-click to run; pin an item to put it on the start page and in the tray. A pinned app starts, or comes to the front when it is running.";
        NewButton.Visibility = EditButton.Visibility = _activitiesOnly ? Visibility.Visible : Visibility.Collapsed;
        State.ConfigurationChanged += State_Changed;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
    }

    private void State_Changed(object? sender, EventArgs e) => Refresh();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        // Saving (a pin, an edit) refreshes the list; keep the selection across it.
        var selected = Selected().Select(item => item.Target).ToHashSet();
        var items = _sort.Apply((_activitiesOnly ? LaunchItem.Activities(State.Config) : LaunchItem.All(State.Config))
            .Where(item => ItemActions.Matches(Search.Text, item.Name, item.Kind)))
            .ToList();
        List.ItemsSource = items;
        foreach (var item in items.Where(item => selected.Contains(item.Target)))
        {
            List.SelectedItems.Add(item);
        }
    }

    private List<LaunchItem> Selected() => List.SelectedItems.OfType<LaunchItem>().ToList();

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LaunchItem item })
        {
            FavoriteOrdering.Toggle(item.Target);
            State.Save();
        }
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            State.LogMessage("Nothing selected.");
            return;
        }

        foreach (var item in selected)
        {
            item.Run();
        }
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var name = await Dialogs.PromptAsync(XamlRoot, "New activity", "Activity name:", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var activity = new Activity { Name = ActivityOperations.UniqueName(State.Config.Activities, name.Trim()) };
        State.Config.Activities.Add(activity);
        State.Save();
        State.LogMessage($"Created activity '{activity.Name}'. Add items to it with Add to activity on any tab.");
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected is not [{ Target: Activity activity }])
        {
            State.LogMessage("Select one activity to edit.");
            return;
        }

        if (await ActivityEditor.EditAsync(XamlRoot, activity))
        {
            State.Save();
            State.LogMessage($"Updated activity '{activity.Name}'.");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ItemActions.DeleteAsync(XamlRoot, Selected().Select(item => item.Target).ToList());

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // A double-click on the pin button is two clicks of that button, not a run of the row.
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LaunchItem item && !IsInButton(e.OriginalSource as DependencyObject))
        {
            item.Run();
        }
    }

    /// <summary>Whether a click inside a list row landed on a button in that row.</summary>
    internal static bool IsInButton(DependencyObject? element)
    {
        for (; element is not null and not ListViewItem; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }
}
