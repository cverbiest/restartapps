using DevWorkspaceLauncher.WinUI.Pages;
using Microsoft.UI.Windowing;
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
    private const string ActionsTag = "page:actions";
    private const string RdpTag = "page:rdp";
    private const string SshTag = "page:ssh";
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
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        RestorePlacement();

        // Closing hides the window to the tray; only Exit in the tray menu ends the app.
        AppWindow.Closing += (sender, args) =>
        {
            SavePlacement();
            if (!App.IsExiting)
            {
                args.Cancel = true;
                sender.Hide();
            }
        };
        Closed += (_, _) => SavePlacement();

        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                if (_maximizeWhenShown)
                {
                    _maximizeWhenShown = false;
                    Presenter.Maximize();
                }

                State.RaiseWindowActivated();
            }
        };

        State.BusyChanged += (_, _) => Working.Visibility = State.Busy ? Visibility.Visible : Visibility.Collapsed;
        State.MessageLogged += (_, message) => ShowStatus(message);

        Nav.MenuItems.Add(_startItem);
        Nav.MenuItems.Add(Page("All items", "", AllItemsTag));
        Nav.MenuItems.Add(Page("Activities", "", ActivitiesTag));
        Nav.MenuItems.Add(Page("VS Code workspaces", "", WorkspacesTag));
        Nav.MenuItems.Add(Page("Actions", "", ActionsTag));
        Nav.MenuItems.Add(Page("RDP sessions", "", RdpTag));
        Nav.MenuItems.Add(Page("SSH hosts", "", SshTag));
        Nav.MenuItems.Add(Page("Activity log", "", LogTag));
        Nav.SelectedItem = _startItem;
        ContentFrame.Navigate(typeof(StartPage));
    }

    // ------------------------------------------------------------- placement

    /// <summary>The bounds when last neither maximized nor minimized: what is saved and restored.</summary>
    private Windows.Graphics.RectInt32 _restoredBounds;

    /// <summary>Set when the saved placement was maximized; maximizing a hidden window would show it.</summary>
    private bool _maximizeWhenShown;

    private OverlappedPresenter Presenter => (OverlappedPresenter)AppWindow.Presenter;

    /// <summary>Puts the window where it was last time, or on the primary monitor when that one is gone.</summary>
    private void RestorePlacement()
    {
        if (WindowPlacementStore.Load() is { } saved)
        {
            var placement = saved.Fit(Displays.Monitors());
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(placement.Left, placement.Top, placement.Width, placement.Height));
            _maximizeWhenShown = placement.Maximized;
        }
        else
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));
        }

        _restoredBounds = CurrentBounds();
        AppWindow.Changed += (_, args) =>
        {
            if ((args.DidPositionChange || args.DidSizeChange) && Presenter.State == OverlappedPresenterState.Restored)
            {
                _restoredBounds = CurrentBounds();
            }
        };
    }

    /// <summary>
    /// Moves the window to the primary monitor when it is not on a connected one - a monitor can be
    /// unplugged while the window is hidden in the tray. Call before showing it.
    /// </summary>
    public void EnsureOnScreen()
    {
        var current = new WindowPlacement(_restoredBounds.X, _restoredBounds.Y, _restoredBounds.Width, _restoredBounds.Height);
        var fitted = current.Fit(Displays.Monitors());
        if (fitted != current)
        {
            if (Presenter.State == OverlappedPresenterState.Maximized)
            {
                Presenter.Restore();
                _maximizeWhenShown = true;
            }

            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(fitted.Left, fitted.Top, fitted.Width, fitted.Height));
        }
    }

    private void SavePlacement()
    {
        try
        {
            WindowPlacementStore.Save(new WindowPlacement(
                _restoredBounds.X,
                _restoredBounds.Y,
                _restoredBounds.Width,
                _restoredBounds.Height,
                Presenter.State == OverlappedPresenterState.Maximized || (_maximizeWhenShown && !AppWindow.IsVisible)));
        }
        catch (Exception ex)
        {
            State.LogMessage($"Could not save the window position: {ex.Message}");
        }
    }

    private Windows.Graphics.RectInt32 CurrentBounds() =>
        new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

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

            case ActionsTag:
                Show(typeof(ActionsPage));
                break;

            case RdpTag:
                Show(typeof(RdpSessionsPage));
                break;

            case SshTag:
                Show(typeof(SshHostsPage));
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

    // Drop an executable or shortcut anywhere on the window to add it as an application action.
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
