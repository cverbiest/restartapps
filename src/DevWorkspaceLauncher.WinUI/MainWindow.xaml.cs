using DevWorkspaceLauncher.WinUI.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DevWorkspaceLauncher.WinUI;

public sealed partial class MainWindow : Window
{
    private const string WorkspacesTag = "page:workspaces";
    private const string AppsTag = "page:apps";
    private const string LogTag = "page:log";

    private static AppState State => App.State;

    private readonly NavigationViewItem _workspacesItem = Page("Workspaces", "", WorkspacesTag);
    private readonly NavigationViewItem _appsItem = Page("Apps", "", AppsTag);
    private readonly NavigationViewItem _logItem = Page("Activity log", "", LogTag);

    private object? _lastRunTag;
    private DateTime _lastRunAt;

    public MainWindow()
    {
        InitializeComponent();

        Title = AppIdentity.DisplayName;
        AppTitle.Text = AppIdentity.DisplayName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));

        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                State.RaiseWindowActivated();
            }
        };

        State.ConfigurationChanged += (_, _) => BuildMenu();
        State.BusyChanged += (_, _) => Working.Visibility = State.Busy ? Visibility.Visible : Visibility.Collapsed;
        State.MessageLogged += (_, message) => ShowStatus(message);

        BuildMenu();
        Nav.SelectedItem = _workspacesItem;
        ContentFrame.Navigate(typeof(WorkspacesPage));
    }

    private static NavigationViewItem Page(string text, string glyph, string tag) =>
        new() { Content = text, Icon = new FontIcon { Glyph = glyph }, Tag = tag };

    /// <summary>
    /// Builds the rail: activities, the pages, then sessions and app profiles. Invoking an activity,
    /// session or profile runs it at once, like the one-click buttons of the WinForms app.
    /// </summary>
    private void BuildMenu()
    {
        var selected = Nav.SelectedItem;
        Nav.MenuItems.Clear();

        Nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Activities" });
        if (State.Config.Activities.Count == 0)
        {
            Nav.MenuItems.Add(Hint("None yet", "Add activities to the configuration file (Settings > Edit Config)."));
        }

        foreach (var activity in State.Config.Activities)
        {
            Nav.MenuItems.Add(Runnable(activity.Name, "", activity, $"Run '{activity.Name}'."));
        }

        Nav.MenuItems.Add(new NavigationViewItemSeparator());
        Nav.MenuItems.Add(_workspacesItem);
        Nav.MenuItems.Add(_appsItem);
        Nav.MenuItems.Add(_logItem);

        if (State.Config.WorkspaceProfiles.Count > 0)
        {
            Nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Sessions" });
            foreach (var session in State.Config.WorkspaceProfiles)
            {
                Nav.MenuItems.Add(Runnable(session.Name, "", session, $"Open the {session.Workspaces.Count} workspace(s) in '{session.Name}'."));
            }
        }

        if (State.Config.Profiles.Count > 0)
        {
            Nav.MenuItems.Add(new NavigationViewItemHeader { Content = "App profiles" });
            foreach (var profile in State.Config.Profiles)
            {
                Nav.MenuItems.Add(Runnable(profile.Name, "", profile, $"Load '{profile.Name}' and run Kill & Start Selected."));
            }
        }

        Nav.SelectedItem = selected;
    }

    private static NavigationViewItem Runnable(string text, string glyph, object tag, string tooltip)
    {
        var item = new NavigationViewItem
        {
            Content = text,
            Icon = new FontIcon { Glyph = glyph },
            Tag = tag,
            SelectsOnInvoked = false
        };
        ToolTipService.SetToolTip(item, tooltip);
        return item;
    }

    private static NavigationViewItem Hint(string text, string tooltip)
    {
        var item = new NavigationViewItem { Content = text, IsEnabled = false, SelectsOnInvoked = false };
        ToolTipService.SetToolTip(item, tooltip);
        return item;
    }

    private async void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            Show(typeof(SettingsPage));
            return;
        }

        var tag = args.InvokedItemContainer?.Tag;
        if (tag is Activity or WorkspaceProfile or Profile && IsRepeat(tag))
        {
            return;
        }

        switch (tag)
        {
            case WorkspacesTag:
                Show(typeof(WorkspacesPage));
                break;

            case AppsTag:
                Show(typeof(AppsPage));
                break;

            case LogTag:
                Show(typeof(LogPage));
                break;

            case Activity activity:
                await State.RunActivityAsync(activity);
                break;

            case WorkspaceProfile session:
                await State.RunSessionAsync(session);
                break;

            case Profile profile:
                await State.RunProfileAsync(profile);
                break;
        }
    }

    private void Show(Type page)
    {
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    /// <summary>
    /// Whether this item was invoked less than a second ago. Items that run something do not stay
    /// selected, and NavigationView can bounce an invocation of such an item - it re-invokes the
    /// previously selected page and then the item again - which must not run the item twice. Keeps a
    /// double-click from running it twice as well.
    /// </summary>
    private bool IsRepeat(object tag)
    {
        var now = DateTime.UtcNow;
        var repeat = ReferenceEquals(tag, _lastRunTag) && now - _lastRunAt < TimeSpan.FromSeconds(1);
        _lastRunTag = tag;
        _lastRunAt = now;
        return repeat;
    }

    /// <summary>Shows the latest log line in the banner; problems as warnings.</summary>
    private void ShowStatus(string message)
    {
        var problem = message.StartsWith("Failed", StringComparison.Ordinal)
            || message.StartsWith("Error", StringComparison.Ordinal)
            || message.StartsWith("Could not", StringComparison.Ordinal);

        Status.Severity = problem ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        Status.Message = message;
        Status.IsOpen = true;
    }

    // Drop an executable or shortcut anywhere on the window to add it to the configuration.
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var added = DroppedApps.Add(State.Config, items.Select(item => item.Path), State.LogMessage);
            if (added.Count > 0)
            {
                State.Save();
            }
        }
        catch (Exception ex)
        {
            State.LogMessage($"Failed to add the dropped files: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
