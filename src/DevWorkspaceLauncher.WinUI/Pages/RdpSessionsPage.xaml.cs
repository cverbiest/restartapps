using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>The saved RDP sessions: connect, add, edit, delete.</summary>
public sealed partial class RdpSessionsPage : Page
{
    private const string DefaultSize = "(full screen)";

    private readonly ObservableCollection<RdpSession> _items = [];

    private static AppState State => App.State;

    public RdpSessionsPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
    }

    private void State_Changed(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _items.Clear();
        foreach (var session in State.Config.RdpSessions)
        {
            _items.Add(session);
        }
    }

    private List<RdpSession> Selected() => List.SelectedItems.OfType<RdpSession>().ToList();

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            State.LogMessage("No RDP session selected.");
            return;
        }

        foreach (var session in selected)
        {
            State.ConnectRdp(session);
        }
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is RdpSession session)
        {
            State.ConnectRdp(session);
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var session = new RdpSession { Name = string.Empty };
        if (!await EditAsync(session, "Add RDP session"))
        {
            return;
        }

        State.Config.RdpSessions.Add(session);
        State.Save();
        State.LogMessage($"Added RDP session '{session.Name}'.");
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count != 1)
        {
            State.LogMessage("Select one RDP session to edit.");
            return;
        }

        if (await EditAsync(selected[0], "Edit RDP session"))
        {
            State.Save();
            State.LogMessage($"Updated RDP session '{selected[0].Name}'.");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            State.LogMessage("No RDP session selected.");
            return;
        }

        var names = string.Join(", ", selected.Select(session => $"'{session.Name}'"));
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Delete RDP sessions", $"Delete {names}?"))
        {
            return;
        }

        foreach (var session in selected)
        {
            State.Config.RdpSessions.Remove(session);
        }

        State.Save();
        State.LogMessage($"Deleted RDP session(s) {names}.");
    }

    /// <summary>
    /// Shows the session's fields in a dialog and writes them back on OK. Only the common fields are
    /// here; anything else (raw options, a base .rdp file, display overrides) is edited in the file.
    /// </summary>
    private async Task<bool> EditAsync(RdpSession session, string title)
    {
        var name = new TextBox { Header = "Name", Text = session.Name };
        var host = new TextBox { Header = "Computer (host or host:port)", Text = session.Host ?? string.Empty };
        var credential = new TextBox
        {
            Header = "Credential (generic credential name in Credential Manager)",
            Text = session.Credential ?? string.Empty,
            PlaceholderText = "Empty: mstsc asks for the password"
        };
        var username = new TextBox
        {
            Header = "Username (optional, overrides the credential's)",
            Text = session.Username ?? string.Empty
        };
        var size = new ComboBox { Header = "Size", HorizontalAlignment = HorizontalAlignment.Stretch };
        size.Items.Add(DefaultSize);
        foreach (var preset in RdpPresets.All(State.Config))
        {
            size.Items.Add(preset.Name);
        }

        size.SelectedItem = size.Items.FirstOrDefault(item => string.Equals((string)item, session.Size, StringComparison.OrdinalIgnoreCase))
            ?? DefaultSize;
        var monitor = new NumberBox
        {
            Header = "Monitor (empty: primary)",
            Value = session.Monitor ?? double.NaN,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var admin = new CheckBox { Content = "Admin / console session", IsChecked = session.Admin == true };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new StackPanel { Spacing = 8, MinWidth = 400, Children = { name, host, credential, username, size, monitor, admin, error } },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var newName = name.Text.Trim();
            var duplicate = State.Config.RdpSessions.Any(other => !ReferenceEquals(other, session)
                && string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase));
            error.Text = newName.Length == 0 ? "Give the session a name."
                : duplicate ? $"There is already a session named '{newName}'."
                : host.Text.Trim().Length == 0 ? "Give the computer to connect to."
                : string.Empty;
            args.Cancel = error.Text.Length > 0;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        session.Name = name.Text.Trim();
        session.Host = host.Text.Trim();
        session.Credential = NullIfEmpty(credential.Text);
        session.Username = NullIfEmpty(username.Text);
        session.Size = size.SelectedItem as string is { } chosen && chosen != DefaultSize ? chosen : null;
        session.Monitor = double.IsNaN(monitor.Value) ? null : (int)monitor.Value;
        session.Admin = admin.IsChecked == true ? true : null;
        return true;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
