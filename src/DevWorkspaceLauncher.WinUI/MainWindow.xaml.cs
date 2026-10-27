using DevWorkspaceLauncher.WinUI.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DevWorkspaceLauncher.WinUI;

public sealed partial class MainWindow : Window
{
    private const string StartTag = "page:start";
    private const string AllItemsTag = "page:all";
    private const string ActivitiesTag = "page:activities";
    private const string WorkspacesTag = "page:workspaces";
    private const string AppsTag = "page:apps";
    private const string RdpTag = "page:rdp";
    private const string LogTag = "page:log";

    private static AppState State => App.State;

    private readonly NavigationViewItem _startItem = Page("Start", "", StartTag);

    /// <summary>The parameter the shown page was opened with; it tells All items from Activities.</summary>
    private string? _shownParameter;

    public MainWindow()
    {
        InitializeComponent();

        Title = AppIdentity.DisplayName;
        AppTitle.Text = AppIdentity.DisplayName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        // Closing hides the window to the tray; only Exit in the tray menu ends the app.
        AppWindow.Closing += (sender, args) =>
        {
            if (!App.IsExiting)
            {
                args.Cancel = true;
                sender.Hide();
            }
        };

        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                State.RaiseWindowActivated();
            }
        };

        State.BusyChanged += (_, _) => Working.Visibility = State.Busy ? Visibility.Visible : Visibility.Collapsed;
        State.MessageLogged += (_, message) => ShowStatus(message);

        Nav.MenuItems.Add(_startItem);
        Nav.MenuItems.Add(Page("All items", "", AllItemsTag));
        Nav.MenuItems.Add(Page("Activities", "", ActivitiesTag));
        Nav.MenuItems.Add(Page("VS Code workspaces", "", WorkspacesTag));
        Nav.MenuItems.Add(Page("Apps", "", AppsTag));
        Nav.MenuItems.Add(Page("RDP sessions", "", RdpTag));
        Nav.MenuItems.Add(Page("Activity log", "", LogTag));
        Nav.SelectedItem = _startItem;
        ContentFrame.Navigate(typeof(StartPage));
    }

    private static NavigationViewItem Page(string text, string glyph, string tag) =>
        new() { Content = text, Icon = new FontIcon { Glyph = glyph }, Tag = tag };

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            Show(typeof(SettingsPage));
            return;
        }

        switch (args.InvokedItemContainer?.Tag)
        {
            case StartTag:
                Show(typeof(StartPage));
                break;

            case AllItemsTag:
                Show(typeof(LaunchItemsPage));
                break;

            case ActivitiesTag:
                Show(typeof(LaunchItemsPage), LaunchItemsPage.ActivitiesOnly);
                break;

            case WorkspacesTag:
                Show(typeof(WorkspacesPage));
                break;

            case AppsTag:
                Show(typeof(AppsPage));
                break;

            case RdpTag:
                Show(typeof(RdpSessionsPage));
                break;

            case LogTag:
                Show(typeof(LogPage));
                break;
        }
    }

    /// <summary>Shows a page; All items and Activities are one page with a different parameter.</summary>
    private void Show(Type page, string? parameter = null)
    {
        if (ContentFrame.CurrentSourcePageType != page || _shownParameter != parameter)
        {
            ContentFrame.Navigate(page, parameter);
            _shownParameter = parameter;
        }
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
