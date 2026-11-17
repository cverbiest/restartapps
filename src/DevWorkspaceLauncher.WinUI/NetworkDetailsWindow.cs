using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// Everything about one network interface, all selectable, with Connect, Copy all, and its display name
/// and hidden flag to edit. Resizable; one window per interface, brought to the front when opened again.
/// </summary>
internal sealed class NetworkDetailsWindow : Window
{
    private const int LogicalWidth = 560;
    private const int LogicalHeight = 640;

    private static readonly Dictionary<string, NetworkDetailsWindow> Open = new(StringComparer.OrdinalIgnoreCase);

    private readonly AppState _state;
    private readonly NetworkItem _item;
    private readonly TextBox _name;
    private readonly CheckBox _hide;
    private readonly AutoSuggestBox _connectWith;
    private readonly List<string> _applications;

    /// <summary>Shows the window for <paramref name="item"/>, or brings the open one to the front.</summary>
    public static void Show(AppState state, NetworkItem item)
    {
        if (Open.TryGetValue(item.Status.Id, out var open))
        {
            open.Activate();
            return;
        }

        var window = new NetworkDetailsWindow(state, item);
        window.Activate();
    }

    private NetworkDetailsWindow(AppState state, NetworkItem item)
    {
        _state = state;
        _item = item;
        var status = item.Status;

        Title = $"{item.Name} - network";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
        }

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new FontIcon { Glyph = item.Glyph, FontSize = 20, Foreground = item.IconBrush });
        heading.Children.Add(new TextBlock
        {
            Text = item.Name,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        // One grid for the details and the fields to edit, each label beside its value.
        var details = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var (label, value) in status.Details(item.Settings))
        {
            AddRow(details, label, new TextBlock
            {
                Text = value,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas")
            });
        }

        _name = new TextBox { Text = item.Settings?.Name ?? string.Empty, PlaceholderText = status.DisplayName(null) };

        // The applications, so a VPN client's app can be what Connect starts.
        _applications = state.Config.Applications.Where(app => !app.Disabled).Select(app => app.Name)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        _connectWith = new AutoSuggestBox
        {
            Text = item.Settings?.Connect ?? string.Empty,
            PlaceholderText = "Windows' dialog - type to search the applications",
            QueryIcon = new SymbolIcon(Symbol.Find)
        };
        ToolTipService.SetToolTip(_connectWith, "What Connect opens: an application from the Actions tab, such as the VPN client. Empty: Windows' own dialog for this kind of connection.");
        var connectWithNote = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            Visibility = Visibility.Collapsed
        };
        var connectWithPanel = new StackPanel { Spacing = 4 };
        connectWithPanel.Children.Add(_connectWith);
        connectWithPanel.Children.Add(connectWithNote);

        _hide = new CheckBox { Content = "Hide on the start page", IsChecked = item.Settings?.Hidden == true };

        AddRow(details, string.Empty, new Border { Height = 8 });
        AddRow(details, "Display name", _name);
        AddRow(details, "Connect with", connectWithPanel);
        AddRow(details, "Start page", _hide);

        var body = details;

        var connect = new Button { Content = NetworkItem.ConnectLabel(status, ConnectWith) };
        void ConnectWithChanged()
        {
            connect.Content = NetworkItem.ConnectLabel(status, ConnectWith);
            var unknown = ConnectWith is { } name && !_applications.Contains(name, StringComparer.OrdinalIgnoreCase);
            connectWithNote.Text = unknown ? "No application with this name; Connect opens Windows' dialog." : string.Empty;
            connectWithNote.Visibility = unknown ? Visibility.Visible : Visibility.Collapsed;
        }

        _connectWith.TextChanged += (box, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                box.ItemsSource = Matching(box.Text);
                box.IsSuggestionListOpen = true;
            }

            ConnectWithChanged();
        };
        _connectWith.GotFocus += (_, _) =>
        {
            _connectWith.ItemsSource = Matching(_connectWith.Text);
            _connectWith.IsSuggestionListOpen = true;
        };
        _connectWith.SuggestionChosen += (box, args) => box.Text = (string)args.SelectedItem;
        ConnectWithChanged();
        connect.Click += (_, _) => NetworkStatus.Connect(_state, _item.Status, ConnectWith);

        var copy = new Button { Content = "Copy all" };
        copy.Click += (_, _) =>
        {
            Clipboard.Copy(status.DetailsText(item.Settings));
            copy.Content = "Copied";
        };

        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) => Save();

        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();

        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(copy, 2);
        Grid.SetColumn(save, 3);
        Grid.SetColumn(close, 4);
        buttons.Children.Add(connect);
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(close);

        var root = new Grid { Padding = new Thickness(20), RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroller, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(heading);
        root.Children.Add(scroller);
        root.Children.Add(buttons);
        Content = root;

        Place();
        Open[status.Id] = this;
        Closed += (_, _) => Open.Remove(status.Id);
    }

    /// <summary>Centred on the display the mouse is on.</summary>
    private void Place()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(LogicalWidth * scale), area.Width);
        var height = Math.Min((int)(LogicalHeight * scale), area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));
    }

    /// <summary>
    /// The application chosen to connect with, in its own spelling; null for Windows' dialog. A name that
    /// matches no application is kept as typed.
    /// </summary>
    private string? ConnectWith
    {
        get
        {
            var text = _connectWith.Text.Trim();
            return text.Length == 0
                ? null
                : _applications.FirstOrDefault(name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) ?? text;
        }
    }

    /// <summary>
    /// The applications whose name contains <paramref name="text"/>; all of them when it is empty or
    /// already names one exactly, so the list opened on a filled-in box offers the others too.
    /// </summary>
    private List<string> Matching(string text)
    {
        text = text.Trim();
        return text.Length == 0 || _applications.Contains(text, StringComparer.OrdinalIgnoreCase)
            ? _applications
            : _applications.Where(name => name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>Adds a row with <paramref name="label"/> beside <paramref name="value"/>.</summary>
    private static void AddRow(Grid grid, string label, FrameworkElement value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelText = new TextBlock
        {
            Text = label,
            IsTextSelectionEnabled = true,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            // Beside a text box or check box, the label sits level with its text.
            VerticalAlignment = value is TextBlock ? VerticalAlignment.Top : VerticalAlignment.Center
        };
        if (value is StackPanel)
        {
            labelText.VerticalAlignment = VerticalAlignment.Top;
            labelText.Margin = new Thickness(0, 6, 0, 0);
        }

        Grid.SetRow(labelText, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(labelText);
        grid.Children.Add(value);
    }

    private void Save()
    {
        NetworkInterfaceSettings.Set(_state.Config, _item.Status, _name.Text, _hide.IsChecked == true, ConnectWith);
        _state.Save();
        Close();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

/// <summary>Puts text on the clipboard.</summary>
internal static class Clipboard
{
    public static void Copy(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }
}
