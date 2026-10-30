using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// The app definitions: add, edit, delete (or drop files on the window), start, stop and restart the
/// selected ones, pin, add to an activity.
/// </summary>
public sealed partial class AppsPage : Page
{
    private readonly ColumnSort<AppItem> _sort;

    private static AppState State => App.State;

    public AppsPage()
    {
        InitializeComponent();
        ItemActions.AttachActivityMenu(ActivityMenu, () => XamlRoot, () => Selected().Cast<object>().ToList());
        _sort = new ColumnSort<AppItem>(Refresh,
            (FavHeader, item => item.App.Favorite ? 0 : 1),
            (NameHeader, item => item.Name),
            (CommandHeader, item => item.Commandline),
            (StopHeader, item => item.StopMethod),
            (StatusHeader, item => item.Running ? 0 : item.App.Disabled ? 2 : 1));
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
        var items = _sort.Apply(State.Config.Apps
            .Where(app => ItemActions.Matches(Search.Text, app.Name, app.Commandline))
            .Select(app => new AppItem(app, !app.Disabled && AppController.IsRunning(app))))
            .ToList();
        List.ItemsSource = items;
        foreach (var item in items.Where(item => selected.Contains(item.App)))
        {
            List.SelectedItems.Add(item);
        }
    }

    /// <summary>The selected apps; <paramref name="enabledOnly"/> leaves out the disabled ones, for the actions that run them.</summary>
    private List<AppConfiguration> Selected(bool enabledOnly = false) =>
        List.SelectedItems.OfType<AppItem>().Select(item => item.App).Where(app => !enabledOnly || !app.Disabled).ToList();

    private async Task RunOnSelectedAsync(Func<IReadOnlyList<AppConfiguration>, Task> run)
    {
        var selected = Selected(enabledOnly: true);
        if (selected.Count == 0)
        {
            State.LogMessage("No (enabled) app selected.");
            return;
        }

        await run(selected);
        Refresh();
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await RunOnSelectedAsync(State.StartAppsAsync);

    private async void Stop_Click(object sender, RoutedEventArgs e) => await RunOnSelectedAsync(State.StopAppsAsync);

    private async void Restart_Click(object sender, RoutedEventArgs e) => await RunOnSelectedAsync(State.RestartAppsAsync);

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is AppItem { App.Disabled: false } item
            && !LaunchItemsPage.IsInButton(e.OriginalSource as DependencyObject))
        {
            State.StartOrFocusApp(item.App);
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppItem item })
        {
            FavoriteOrdering.Toggle(item.App);
            State.Save();
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppConfiguration { Name = string.Empty, Commandline = string.Empty };
        if (await EditAsync(app, "Add app"))
        {
            State.Config.Apps.Add(app);
            State.Save();
            State.LogMessage($"Added app '{app.Name}'.");
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not [var app])
        {
            State.LogMessage("Select one app to edit.");
            return;
        }

        var oldName = app.Name;
        if (await EditAsync(app, "Edit app"))
        {
            if (!string.Equals(oldName, app.Name, StringComparison.Ordinal))
            {
                ActivityOperations.RenameApp(State.Config, oldName, app.Name);
            }

            State.Save();
            State.LogMessage($"Updated app '{app.Name}'.");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ItemActions.DeleteAsync(XamlRoot, Selected().Cast<object>().ToList());

    private void EditConfig_Click(object sender, RoutedEventArgs e) =>
        ConfigFileOpener.Open(State.ConfigPath, State.LogMessage);

    /// <summary>Shows the app's fields in a dialog and writes them back on OK.</summary>
    private async Task<bool> EditAsync(AppConfiguration app, string title)
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
            var duplicate = State.Config.Apps.Any(other => !ReferenceEquals(other, app)
                && string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase));
            error.Text = newName.Length == 0 ? "Give the app a name."
                : duplicate ? $"There is already an app named '{newName}'."
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
