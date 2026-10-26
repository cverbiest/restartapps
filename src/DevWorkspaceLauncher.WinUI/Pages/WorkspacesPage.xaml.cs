using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>The VS Code workspaces: which are open, which to open or close, and sessions of them.</summary>
public sealed partial class WorkspacesPage : Page
{
    private readonly ObservableCollection<WorkspaceItem> _items = [];
    private readonly DispatcherQueueTimer _activationRefresh;

    private static AppState State => App.State;

    public WorkspacesPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;

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
        var rows = WorkspaceController.LoadRows(State.Config, State.ShowAllWorkspaces, State.WorkspaceTicks);

        _items.Clear();
        foreach (var row in rows)
        {
            _items.Add(new WorkspaceItem(row));
        }

        Count.Text = State.ShowAllWorkspaces ? $"{rows.Count} recent" : $"{rows.Count} preferred";
        BuildSessionMenu();
    }

    // ------------------------------------------------------------------ rows

    private void Close_Click(object sender, RoutedEventArgs e) => UpdateTick(sender, (item, ticked) => item.Close = ticked);

    private void Open_Click(object sender, RoutedEventArgs e) => UpdateTick(sender, (item, ticked) => item.Open = ticked);

    private static void UpdateTick(object sender, Action<WorkspaceItem, bool> set)
    {
        if (sender is CheckBox { DataContext: WorkspaceItem item } box)
        {
            set(item, box.IsChecked == true);
            State.WorkspaceTicks[item.Uri] = (item.Close, item.Open);
        }
    }

    /// <summary>Preferred is a stated preference, so unlike the ticks it is saved at once.</summary>
    private void Preferred_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItem item })
        {
            WorkspaceController.TogglePreferred(State.Config, item.Row, State.LogMessage);
            State.Save();
        }
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItem item })
        {
            WorkspaceController.Launch(item.Row, State.Config.VsCodeExecutable, State.LogMessage);
        }
    }

    // ---------------------------------------------------------------- toolbar

    private async void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        var ticked = _items.Where(item => item.Open).Select(item => (item.Uri, item.Name)).ToList();
        if (ticked.Count == 0)
        {
            State.LogMessage("No workspace is ticked Open.");
            return;
        }

        await State.RunBusyAsync(() => WorkspaceController.OpenAsync(ticked, State.Config.VsCodeExecutable, State.LogMessage));
        Refresh();
    }

    private async void CloseSelected_Click(object sender, RoutedEventArgs e)
    {
        var ticked = _items.Where(item => item.Close && item.IsOpen).ToList();
        if (ticked.Count == 0)
        {
            State.LogMessage("No open workspace is ticked Close.");
            return;
        }

        await State.RunBusyAsync(async () =>
        {
            foreach (var item in ticked)
            {
                await WorkspaceController.CloseAsync(item.Name, item.Row.WindowTitle, State.LogMessage);
            }
        });
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        State.ShowAllWorkspaces = ShowAll.IsChecked == true;
        Refresh();
    }

    // --------------------------------------------------------------- sessions

    private WorkspaceProfile? SelectedSession() => SessionOperations.Find(State.Config.WorkspaceProfiles, State.SelectedSessionName);

    private void BuildSessionMenu()
    {
        if (SelectedSession() is null)
        {
            State.SelectedSessionName = State.Config.WorkspaceProfiles.FirstOrDefault()?.Name;
        }

        SessionButton.Content = State.SelectedSessionName is null ? "Session" : $"Session: {State.SelectedSessionName}";
        SessionMenu.Items.Clear();

        foreach (var session in State.Config.WorkspaceProfiles)
        {
            var choice = new RadioMenuFlyoutItem
            {
                Text = session.Name,
                GroupName = "Session",
                IsChecked = string.Equals(session.Name, State.SelectedSessionName, StringComparison.OrdinalIgnoreCase)
            };
            choice.Click += (_, _) =>
            {
                State.SelectedSessionName = session.Name;
                BuildSessionMenu();
            };
            SessionMenu.Items.Add(choice);
        }

        if (State.Config.WorkspaceProfiles.Count > 0)
        {
            SessionMenu.Items.Add(new MenuFlyoutSeparator());
        }

        SessionMenu.Items.Add(Command("Load", "Tick this session's workspaces in the list.", LoadSession));
        SessionMenu.Items.Add(Command("Save As…", "Store the ticked Open workspaces as a new session.", SaveSessionAs));
        SessionMenu.Items.Add(Command("Update", "Overwrite the selected session with the ticked Open workspaces.", UpdateSession));
        SessionMenu.Items.Add(Command("Delete", "Remove the selected session.", DeleteSession));
    }

    private static MenuFlyoutItem Command(string text, string tooltip, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        ToolTipService.SetToolTip(item, tooltip);
        item.Click += (_, _) => onClick();
        return item;
    }

    private List<string> TickedWorkspaceUris() => _items.Where(item => item.Open).Select(item => item.Uri).ToList();

    private void LoadSession()
    {
        var session = SelectedSession();
        if (session is null)
        {
            State.LogMessage("No session selected.");
            return;
        }

        State.ApplySession(session);
    }

    private async void SaveSessionAs()
    {
        var ticked = TickedWorkspaceUris();
        if (ticked.Count == 0)
        {
            State.LogMessage("Tick Open on the workspaces the session should open first.");
            return;
        }

        var name = await Dialogs.PromptAsync(XamlRoot, "Save session as", "Session name:", State.SelectedSessionName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        var existing = SessionOperations.Find(State.Config.WorkspaceProfiles, name);
        if (existing is not null
            && !await Dialogs.ConfirmAsync(XamlRoot, "Save session as", $"Session '{existing.Name}' already exists. Overwrite it?"))
        {
            return;
        }

        var session = SessionOperations.Upsert(State.Config.WorkspaceProfiles, name, ticked);
        State.LogMessage(existing is null
            ? $"Created session '{name}' ({ticked.Count} workspace(s))."
            : $"Overwrote session '{session.Name}' ({ticked.Count} workspace(s)).");

        State.SelectedSessionName = session.Name;
        State.Save();
    }

    private void UpdateSession()
    {
        var session = SelectedSession();
        if (session is null)
        {
            State.LogMessage("No session selected.");
            return;
        }

        var ticked = TickedWorkspaceUris();
        if (ticked.Count == 0)
        {
            State.LogMessage("Tick Open on the workspaces the session should open first.");
            return;
        }

        session.Workspaces = ticked;
        State.Save();
        State.LogMessage($"Updated session '{session.Name}' ({ticked.Count} workspace(s)).");
    }

    private async void DeleteSession()
    {
        var session = SelectedSession();
        if (session is null)
        {
            State.LogMessage("No session selected.");
            return;
        }

        if (!await Dialogs.ConfirmAsync(XamlRoot, "Delete session", $"Delete session '{session.Name}'?"))
        {
            return;
        }

        State.Config.WorkspaceProfiles.Remove(session);
        State.SelectedSessionName = null;
        State.Save();
        State.LogMessage($"Deleted session '{session.Name}'.");
    }
}
