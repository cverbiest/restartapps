using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// The VS Code workspaces: which are open and when each was last used; open, close, remove or add the
/// selected ones to an activity; sort by a column.
/// </summary>
public sealed partial class WorkspacesPage : Page
{
    private readonly ObservableCollection<WorkspaceItem> _items = [];
    private readonly DispatcherQueueTimer _activationRefresh;

    /// <summary>The rows' Open and Close ticks are not used here; the selection took their place.</summary>
    private static readonly Dictionary<string, (bool Close, bool Open)> NoTicks = [];

    private readonly ColumnSort<WorkspaceRow> _sort;

    private static AppState State => App.State;

    public WorkspacesPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        ItemActions.AttachActivityMenu(ActivityMenu, () => XamlRoot, () => Selected().Select(item => (object)item.Row).ToList());
        _sort = new ColumnSort<WorkspaceRow>(Refresh,
            (PrefHeader, row => row.Preferred ? 0 : 1),
            (NameHeader, row => VsCodeUri.Title(row.Name, row.Uri)),
            (LocationHeader, row => row.Location),
            (LastUsedHeader, row => row.LastUsed ?? DateTime.MinValue),
            (StatusHeader, row => row.IsOpen ? 0 : 1));

        // Coming back to the window is the moment the open/closed state is most likely stale.
        _activationRefresh = DispatcherQueue.CreateTimer();
        _activationRefresh.Interval = TimeSpan.FromMilliseconds(300);
        _activationRefresh.IsRepeating = false;
        _activationRefresh.Tick += (_, _) => Refresh();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        State.WorkspacesChanged += State_Changed;
        State.WindowActivated += State_WindowActivated;
        ShowAll.IsChecked = State.ShowAllWorkspaces;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
        State.WorkspacesChanged -= State_Changed;
        State.WindowActivated -= State_WindowActivated;
        _activationRefresh.Stop();
    }

    private void State_Changed(object? sender, EventArgs e) => Refresh();

    private void State_WindowActivated(object? sender, EventArgs e)
    {
        _activationRefresh.Stop();
        _activationRefresh.Start();
    }

    /// <summary>Re-reads the workspace list and re-checks which of the shown rows are open.</summary>
    private void Refresh()
    {
        var rows = _sort.Apply(WorkspaceController.LoadRows(State.Config, State.ShowAllWorkspaces, NoTicks)
            .Where(row => ItemActions.Matches(Search.Text, VsCodeUri.Title(row.Name, row.Uri), row.Location)))
            .ToList();

        // A refresh happens whenever the window is activated; keep the selection across it.
        var selected = Selected().Select(item => item.Uri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _items.Clear();
        foreach (var row in rows)
        {
            var item = new WorkspaceItem(row);
            _items.Add(item);
            if (selected.Contains(row.Uri))
            {
                List.SelectedItems.Add(item);
            }
        }

        Count.Text = State.ShowAllWorkspaces ? $"{rows.Count} recent" : $"{rows.Count} preferred";
    }

    private List<WorkspaceItem> Selected() => List.SelectedItems.OfType<WorkspaceItem>().ToList();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    // ------------------------------------------------------------------ rows

    /// <summary>Preferred is a stated preference, so it is saved at once.</summary>
    private void Preferred_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItem item })
        {
            WorkspaceController.TogglePreferred(State.Config, item.Row, State.LogMessage);
            State.Save();
        }
    }

    /// <summary>Opens the workspace, or brings its window to the front when it is open.</summary>
    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is WorkspaceItem item
            && !LaunchItemsPage.IsInButton(e.OriginalSource as DependencyObject))
        {
            WorkspaceController.Launch(item.Row, State.Config.VsCodeExecutable, State.LogMessage);
        }
    }

    // ---------------------------------------------------------------- toolbar

    private async void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected().Select(item => (item.Uri, item.Name)).ToList();
        if (selected.Count == 0)
        {
            State.LogMessage("No workspace selected.");
            return;
        }

        await State.RunBusyAsync(() => WorkspaceController.OpenAsync(selected, State.Config.VsCodeExecutable, State.LogMessage));
        Refresh();
    }

    private async void CloseSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected().Where(item => item.IsOpen).ToList();
        if (selected.Count == 0)
        {
            State.LogMessage("None of the selected workspaces is open.");
            return;
        }

        await State.RunBusyAsync(async () =>
        {
            foreach (var item in selected)
            {
                await WorkspaceController.CloseAsync(item.Name, item.Row.WindowTitle, State.LogMessage);
            }
        });
        Refresh();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ItemActions.DeleteAsync(XamlRoot, Selected().Select(item => (object)item.Row).ToList());

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        State.ShowAllWorkspaces = ShowAll.IsChecked == true;
        Refresh();
    }
}
