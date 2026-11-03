using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// The actions: applications (add, edit, delete, or drop files on the window; start, stop, restart) and
/// the built-in actions (run). Pin either, or add them to an activity.
/// </summary>
public sealed partial class ActionsPage : Page
{
    private readonly ColumnSort<ActionItem> _sort;

    private static AppState State => App.State;

    public ActionsPage()
    {
        InitializeComponent();
        ItemActions.AttachActivityMenu(ActivityMenu, () => XamlRoot, () => Selected().Cast<object>().ToList());
        _sort = new ColumnSort<ActionItem>(Refresh,
            (FavHeader, item => item.Target.Favorite ? 0 : 1),
            (NameHeader, item => item.Name),
            (KindHeader, item => item.Kind),
            (DetailsHeader, item => item.Details),
            (StopHeader, item => item.StopMethod),
            (StatusHeader, item => item.Running ? 0 : item.Disabled ? 2 : 1));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        State.BusyChanged += State_BusyChanged;
        Refresh();
        State_BusyChanged(null, EventArgs.Empty);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
        State.BusyChanged -= State_BusyChanged;
    }

    private void State_Changed(object? sender, EventArgs e) => Refresh();

    private void State_BusyChanged(object? sender, EventArgs e)
    {
        StartButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = !State.Busy;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        // Saving (a pin, an edit) refreshes the list; keep the selection across it.
        var selected = Selected().ToHashSet();
        var items = _sort.Apply(State.Config.Actions
            .Select(action => new ActionItem(action, action is ApplicationAction { Disabled: false } app && AppController.IsRunning(app)))
            .Where(item => ItemActions.Matches(Search.Text, item.Name, item.Kind, item.Details)))
            .ToList();
        List.ItemsSource = items;
        foreach (var item in items.Where(item => selected.Contains(item.Target)))
        {
            List.SelectedItems.Add(item);
        }
    }

    private List<ActionConfiguration> Selected() =>
        List.SelectedItems.OfType<ActionItem>().Select(item => item.Target).ToList();

    /// <summary>The selected applications that are not disabled, for the buttons that run them.</summary>
    private List<ApplicationAction> SelectedApplications() =>
        Selected().OfType<ApplicationAction>().Where(app => !app.Disabled).ToList();

    private async Task RunOnSelectedAsync(Func<IReadOnlyList<ApplicationAction>, Task> run, string verb)
    {
        var builtIns = Selected().OfType<BuiltInAction>().ToList();
        if (builtIns.Count > 0)
        {
            State.LogMessage($"Built-in actions cannot {verb}; {string.Join(", ", builtIns.Select(action => $"'{action.Name}'"))} skipped.");
        }

        await RunApplicationsAsync(run);
    }

    private async Task RunApplicationsAsync(Func<IReadOnlyList<ApplicationAction>, Task> run)
    {
        var selected = SelectedApplications();
        if (selected.Count > 0)
        {
            await run(selected);
        }
        else if (!Selected().OfType<BuiltInAction>().Any())
        {
            State.LogMessage("No (enabled) action selected.");
        }

        Refresh();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        foreach (var builtIn in Selected().OfType<BuiltInAction>())
        {
            State.RunAction(builtIn);
        }

        await RunApplicationsAsync(State.StartAppsAsync);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await RunOnSelectedAsync(State.StopAppsAsync, "stop");

    private async void Restart_Click(object sender, RoutedEventArgs e) => await RunOnSelectedAsync(State.RestartAppsAsync, "restart");

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ActionItem { Disabled: false } item
            && !LaunchItemsPage.IsInButton(e.OriginalSource as DependencyObject))
        {
            State.RunAction(item.Target);
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActionItem item })
        {
            FavoriteOrdering.Toggle(item.Target);
            State.Save();
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var app = new ApplicationAction { Name = string.Empty, Commandline = string.Empty };
        if (await EditAsync(app, "Add application"))
        {
            State.Config.Actions.Add(app);
            State.Save();
            State.LogMessage($"Added application '{app.Name}'.");
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        switch (Selected())
        {
            case [BuiltInAction builtIn]:
                State.LogMessage($"'{builtIn.Name}' is built in; it has nothing to edit.");
                return;

            case [ApplicationAction app]:
                var oldName = app.Name;
                if (await EditAsync(app, "Edit application"))
                {
                    if (!string.Equals(oldName, app.Name, StringComparison.Ordinal))
                    {
                        ActivityOperations.RenameAction(State.Config, oldName, app.Name);
                    }

                    State.Save();
                    State.LogMessage($"Updated application '{app.Name}'.");
                }

                return;

            default:
                State.LogMessage("Select one application to edit.");
                return;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ItemActions.DeleteAsync(XamlRoot, Selected().Cast<object>().ToList());

    private void EditConfig_Click(object sender, RoutedEventArgs e) =>
        ConfigFileOpener.Open(State.ConfigPath, State.LogMessage);

    /// <summary>Shows the application's fields in a dialog and writes them back on OK.</summary>
    private async Task<bool> EditAsync(ApplicationAction app, string title)
    {
        var name = new TextBox { Header = "Name", Text = app.Name };
        var commandline = new TextBox { Header = "Command line (%VARIABLES% allowed)", Text = app.Commandline };
        var stopMethod = new ComboBox
        {
            Header = "Stop by",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Enum.GetNames<StopMethod>(),
            SelectedItem = app.StopMethod.ToString()
        };
        var aumid = new TextBox
        {
            Header = "Window AppUserModelID (for Stop by Window; * and ? allowed)",
            Text = app.WindowAumid ?? string.Empty
        };
        var disabled = new CheckBox { Content = "Disabled (kept in the file, ignored everywhere)", IsChecked = app.Disabled };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new StackPanel { Spacing = 8, MinWidth = 440, Children = { name, commandline, stopMethod, aumid, disabled, error } },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var newName = name.Text.Trim();
            var duplicate = State.Config.Actions.Any(other => !ReferenceEquals(other, app)
                && string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase));
            error.Text = newName.Length == 0 ? "Give the application a name."
                : duplicate ? $"There is already an action named '{newName}'."
                : commandline.Text.Trim().Length == 0 ? "Give the command line that starts it."
                : (string)stopMethod.SelectedItem == nameof(StopMethod.Window) && aumid.Text.Trim().Length == 0
                    ? "Stop by Window needs the window AppUserModelID."
                : string.Empty;
            args.Cancel = error.Text.Length > 0;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        app.Name = name.Text.Trim();
        app.Commandline = commandline.Text.Trim();
        app.StopMethod = Enum.Parse<StopMethod>((string)stopMethod.SelectedItem);
        app.WindowAumid = string.IsNullOrWhiteSpace(aumid.Text) ? null : aumid.Text.Trim();
        app.Disabled = disabled.IsChecked == true;
        if (!app.CanStop)
        {
            app.Kill = false;
        }

        return true;
    }
}
