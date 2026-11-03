using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// A small always-on-top window for one RDP connection. It says it is connecting and closes itself once
/// the session is up; when the connection fails it stays with the reason, Retry and Close, and a button
/// for the fix when there is one. It shows from the tray too, while the main window is hidden.
/// </summary>
internal sealed class RdpConnectWindow : Window
{
    private const int LogicalWidth = 460;
    private const int LogicalHeight = 200;
    private const int Gap = 12;

    /// <summary>The windows showing, oldest first; each new one goes below the last.</summary>
    private static readonly List<RdpConnectWindow> Showing = [];

    private readonly AppState _state;
    private readonly RdpSession _session;
    private readonly CancellationTokenSource _closed = new();

    private readonly ProgressRing _progress = new() { Width = 20, Height = 20, IsActive = true };
    private readonly FontIcon _failed = new() { Glyph = "", FontSize = 20, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
    private readonly TextBlock _heading = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly Button _fix = new();
    private readonly Button _retry = new() { Content = "Retry", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    private readonly Button _close = new() { Content = "Close" };

    private RdpFix _shownFix;

    /// <summary>The mstsc that failed and still shows its error; ended on Retry and Close.</summary>
    private System.Diagnostics.Process? _failedMstsc;

    /// <summary>Connects to <paramref name="session"/>, showing the window while it does.</summary>
    public static void Connect(AppState state, RdpSession session)
    {
        var window = new RdpConnectWindow(state, session);
        window.Activate();
        window.Attempt();
    }

    private RdpConnectWindow(AppState state, RdpSession session)
    {
        _state = state;
        _session = session;

        Title = $"{session.Name} - Remote Desktop";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        _fix.Click += (_, _) => ApplyFix();
        _retry.Click += (_, _) => Attempt();
        _close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_fix);
        buttons.Children.Add(_retry);
        buttons.Children.Add(_close);

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(_progress);
        heading.Children.Add(_failed);
        heading.Children.Add(_heading);

        var root = new Grid { Padding = new Thickness(20), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_message, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(heading);
        root.Children.Add(_message);
        root.Children.Add(buttons);
        Content = root;

        Place();
        Showing.Add(this);
        Closed += (_, _) =>
        {
            _closed.Cancel();
            DismissFailed();
            Showing.Remove(this);
        };
    }

    /// <summary>
    /// Top left of the primary monitor's working area, clear of the middle where mstsc asks for
    /// credentials and shows its own errors; below the windows already showing.
    /// </summary>
    private void Place()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var width = (int)(LogicalWidth * scale);
        var height = (int)(LogicalHeight * scale);
        var gap = (int)(Gap * scale);
        var area = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + gap,
            area.Y + gap + (height + gap) * Showing.Count,
            width,
            height));
    }

    /// <summary>Launches mstsc and waits for the session; the window closes when it is connected.</summary>
    private async void Attempt()
    {
        // Looked up again, so a retry uses the session as it is saved now.
        var session = _state.Config.RdpSessions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, _session.Name, StringComparison.OrdinalIgnoreCase)) ?? _session;

        DismissFailed();
        ShowConnecting();
        try
        {
            var (mstsc, _) = RdpLauncher.Launch(RdpResolver.Resolve(_state.Config, session), session.Name, _state.LogMessage);
            switch (await RdpLauncher.WaitForConnectionAsync(mstsc, _closed.Token))
            {
                case RdpConnectOutcome.Connected:
                    Close();
                    return;
                case RdpConnectOutcome.Failed:
                    _failedMstsc = mstsc;
                    ShowFailure("Remote Desktop could not connect; its message says why. Retry or Close also closes that message.", RdpFix.None);
                    return;
                default:
                    ShowFailure("mstsc closed before the session was connected.", RdpFix.None);
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // The window was closed while connecting; mstsc carries on by itself.
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message, (ex as RdpLaunchException)?.Fix ?? RdpFix.None);
        }
    }

    private void ShowConnecting()
    {
        _heading.Text = $"Connecting to {_session.Name}...";
        _message.Text = "This closes when the session is connected.";
        _progress.Visibility = Visibility.Visible;
        _failed.Visibility = Visibility.Collapsed;
        _fix.Visibility = Visibility.Collapsed;
        _retry.Visibility = Visibility.Collapsed;
    }

    private void ShowFailure(string message, RdpFix fix)
    {
        _state.LogMessage($"Could not connect to '{_session.Name}': {message}");
        _heading.Text = $"Could not connect to {_session.Name}";
        _message.Text = message;
        _progress.Visibility = Visibility.Collapsed;
        _failed.Visibility = Visibility.Visible;
        _retry.Visibility = Visibility.Visible;

        _shownFix = fix;
        _fix.Content = fix switch
        {
            RdpFix.OpenCredentialManager => "Open Credential Manager",
            _ => null
        };
        _fix.Visibility = fix == RdpFix.None ? Visibility.Collapsed : Visibility.Visible;
        Activate();
    }

    private void DismissFailed()
    {
        if (_failedMstsc is { } mstsc)
        {
            _failedMstsc = null;
            RdpLauncher.Dismiss(mstsc);
        }
    }

    private void ApplyFix()
    {
        switch (_shownFix)
        {
            case RdpFix.OpenCredentialManager:
                CredentialStore.OpenManager(_state.LogMessage);
                break;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
