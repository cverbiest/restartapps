using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>The saved RDP sessions: connect, add, edit, delete, pin as favorite.</summary>
public sealed partial class RdpSessionsPage : Page
{
    private const string DefaultSize = "(full screen)";

    private readonly ObservableCollection<RdpSession> _items = [];
    private readonly ColumnSort<RdpSession> _sort;

    private static AppState State => App.State;

    public RdpSessionsPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        ItemActions.AttachActivityMenu(ActivityMenu, () => XamlRoot, () => Selected().Cast<object>().ToList());
        _sort = new ColumnSort<RdpSession>(Refresh,
            (FavHeader, session => session.Favorite ? 0 : 1),
            (NameHeader, session => session.Name),
            (HostHeader, session => session.Host),
            (SizeHeader, session => session.SizeText),
            (CredentialHeader, session => session.Credential));
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

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        // Saving (a pin, an edit) refreshes the list; keep the selection across it.
        var selected = Selected().Select(session => session.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _items.Clear();
        foreach (var session in _sort.Apply(State.Config.RdpSessions.Where(session =>
            ItemActions.Matches(Search.Text, session.Name, session.Host, session.Credential))))
        {
            _items.Add(session);
            if (selected.Contains(session.Name))
            {
                List.SelectedItems.Add(session);
            }
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
        if ((e.OriginalSource as FrameworkElement)?.DataContext is RdpSession session
            && !LaunchItemsPage.IsInButton(e.OriginalSource as DependencyObject))
        {
            State.ConnectRdp(session);
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RdpSession session })
        {
            FavoriteOrdering.Toggle(session);
            State.Save();
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

        var oldName = selected[0].Name;
        if (await EditAsync(selected[0], "Edit RDP session"))
        {
            if (!string.Equals(oldName, selected[0].Name, StringComparison.Ordinal))
            {
                ActivityOperations.RenameRdpSession(State.Config, oldName, selected[0].Name);
            }

            State.Save();
            State.LogMessage($"Updated RDP session '{selected[0].Name}'.");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ItemActions.DeleteAsync(XamlRoot, Selected().Cast<object>().ToList());

    private void Credentials_Click(object sender, RoutedEventArgs e) => CredentialStore.OpenManager(State.LogMessage);

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var entries = RdpPlusImport.ParseListing(await File.ReadAllTextAsync(file.Path));
            if (entries.Count == 0)
            {
                State.LogMessage($"Could not find any favorites in {file.Path}. Expected one 'name | secure=False | command line' per line.");
                return;
            }

            var result = RdpPlusImport.AddTo(State.Config, entries);
            foreach (var entry in entries.Where(entry => entry.Unsupported.Count > 0))
            {
                State.LogMessage($"Imported '{entry.Name}' without {string.Join(" ", entry.Unsupported)} (not supported yet).");
            }

            if (result.Skipped.Count > 0)
            {
                State.LogMessage($"Skipped {result.Skipped.Count} favorite(s) whose name is already a session: {string.Join(", ", result.Skipped)}.");
            }

            if (result.Added.Count > 0)
            {
                State.Save();
            }

            var credentials = result.Added.Select(session => session.Credential).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            var missing = credentials.Where(name => CredentialStore.ReadGeneric(name) is null).ToList();
            var suffix = missing.Count == 0 ? string.Empty
                : $" Add these generic credentials in Credential Manager: {string.Join(", ", missing)}.";
            State.LogMessage($"Imported {result.Added.Count} RDP session(s) from Remote Desktop Plus.{suffix}");
        }
        catch (Exception ex)
        {
            State.LogMessage($"Failed to import {file.Path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows the session's fields in a dialog and writes them back on OK. Only the common fields are
    /// here; anything else (raw options, a base .rdp file, display overrides) is edited in the file.
    /// </summary>
    private async Task<bool> EditAsync(RdpSession session, string title)
    {
        var name = new TextBox { Header = "Name", Text = session.Name };
        var host = new TextBox { Header = "Computer (host or host:port)", Text = session.Host ?? string.Empty };
        // Editable: pick one of the DevLauncher_ credentials, or type any other generic credential's name.
        var known = ListCredentials();
        var credential = new ComboBox
        {
            Header = $"Credential (generic credential in Credential Manager; {CredentialStore.SessionCredentialPrefix}* listed)",
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Empty: mstsc asks for the password",
            ItemsSource = known.Select(item => item.Name).ToList(),
            Text = session.Credential ?? string.Empty
        };
        var credentialUser = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        };

        void ShowCredentialUser(string text)
        {
            text = text.Trim();
            var match = known.FirstOrDefault(item => string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase));
            credentialUser.Text = text.Length == 0 ? string.Empty
                : match.Name is not null ? $"Logs in as {match.UserName ?? "(no username)"}"
                : text.StartsWith(CredentialStore.SessionCredentialPrefix, StringComparison.OrdinalIgnoreCase)
                    ? "Not in Credential Manager yet."
                    : string.Empty;
        }

        credential.SelectionChanged += (_, _) => ShowCredentialUser(credential.SelectedItem as string ?? credential.Text);
        credential.TextSubmitted += (_, args) => ShowCredentialUser(args.Text);
        ShowCredentialUser(credential.Text);
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
        var windowTitle = new TextBox
        {
            Header = "Window title (optional; %s = computer, %u = user)",
            Text = session.Title ?? string.Empty
        };
        var admin = new CheckBox { Content = "Admin / console session", IsChecked = session.Admin == true };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new StackPanel { Spacing = 8, MinWidth = 400, Children = { name, host, credential, credentialUser, username, size, monitor, windowTitle, admin, error } },
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
        // Text, not SelectedItem: after picking an item and then typing, SelectedItem is stale.
        session.Credential = NullIfEmpty(credential.Text);
        session.Username = NullIfEmpty(username.Text);
        session.Size = size.SelectedItem as string is { } chosen && chosen != DefaultSize ? chosen : null;
        session.Monitor = double.IsNaN(monitor.Value) ? null : (int)monitor.Value;
        session.Title = NullIfEmpty(windowTitle.Text);
        session.Admin = admin.IsChecked == true ? true : null;
        return true;
    }

    /// <summary>The DevLauncher_ credentials, or none when Credential Manager cannot be read.</summary>
    private static IReadOnlyList<(string Name, string? UserName)> ListCredentials()
    {
        try
        {
            return CredentialStore.ListGeneric(CredentialStore.SessionCredentialPrefix);
        }
        catch (Exception ex)
        {
            State.LogMessage($"Could not list credentials: {ex.Message}");
            return [];
        }
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
