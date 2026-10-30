using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// The hosts in ~/.ssh/config: connect, add, edit, delete, pin as favorite. The file is the list; edits
/// are written into it, leaving everything else in it as it was.
/// </summary>
public sealed partial class SshHostsPage : Page
{
    private readonly ObservableCollection<SshHost> _items = [];
    private readonly ColumnSort<SshHost> _sort;

    private static AppState State => App.State;

    public SshHostsPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Intro.Text = $"The hosts in {State.SshConfigPath}. Double-click a host to connect (in WezTerm or Windows Terminal, as chosen in Settings); pin it to put it on the start page and in the tray. Edit changes the host name, user and identity file; anything else is edited in the file itself.";
        ItemActions.AttachActivityMenu(ActivityMenu, () => XamlRoot, () => Selected().Cast<object>().ToList());
        _sort = new ColumnSort<SshHost>(Refresh,
            (FavHeader, host => host.Favorite ? 0 : 1),
            (AliasHeader, host => host.Alias),
            (HostNameHeader, host => host.HostName),
            (UserHeader, host => host.User),
            (IdentityHeader, host => host.IdentityFile),
            (AlsoHeader, host => host.Also));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        State.ReloadSshHosts();
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
        var selected = Selected().Select(host => host.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _items.Clear();
        foreach (var host in _sort.Apply(State.SshHosts.Where(host =>
            ItemActions.Matches(Search.Text, host.Alias, host.HostName, host.User, host.IdentityFile, host.Also))))
        {
            _items.Add(host);
            if (selected.Contains(host.Alias))
            {
                List.SelectedItems.Add(host);
            }
        }
    }

    private List<SshHost> Selected() => List.SelectedItems.OfType<SshHost>().ToList();

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            State.LogMessage("No SSH host selected.");
            return;
        }

        foreach (var host in selected)
        {
            State.ConnectSsh(host);
        }
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SshHost host
            && !LaunchItemsPage.IsInButton(e.OriginalSource as DependencyObject))
        {
            State.ConnectSsh(host);
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SshHost host })
        {
            FavoriteOrdering.Toggle(host);
            State.Save();
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (await EditAsync(null, "Add SSH host") is { } fields
            && State.EditSshConfig(document =>
            {
                document.Add(fields);
                return true;
            }))
        {
            State.LogMessage($"Added SSH host '{fields.Alias}'.");
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count != 1)
        {
            State.LogMessage("Select one SSH host to edit.");
            return;
        }

        var oldAlias = selected[0].Alias;
        if (await EditAsync(selected[0].Entry, "Edit SSH host") is not { } fields
            || !State.EditSshConfig(document => document.Update(oldAlias, fields)))
        {
            return;
        }

        if (!string.Equals(oldAlias, fields.Alias, StringComparison.Ordinal))
        {
            ActivityOperations.RenameSshHost(State.Config, oldAlias, fields.Alias);
            State.Save();
        }

        State.LogMessage($"Updated SSH host '{fields.Alias}'.");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ItemActions.DeleteAsync(XamlRoot, Selected().Cast<object>().ToList());

    private void OpenFile_Click(object sender, RoutedEventArgs e) => ConfigFileOpener.Open(State.SshConfigPath, State.LogMessage);

    /// <summary>Shows the host's fields in a dialog; returns them on OK, null on Cancel.</summary>
    private async Task<SshHostFields?> EditAsync(SshHostEntry? entry, string title)
    {
        var alias = new TextBox { Header = "Host (the alias you connect to)", Text = entry?.Alias ?? string.Empty };
        var hostName = new TextBox { Header = "Host name (empty: the alias itself)", Text = entry?.HostName ?? string.Empty };
        var user = new TextBox { Header = "User (empty: your Windows user name)", Text = entry?.User ?? string.Empty };
        var identityFile = new TextBox
        {
            Header = "Identity file (optional, e.g. ~/.ssh/id_ed25519)",
            Text = entry?.IdentityFile ?? string.Empty
        };
        var also = string.Join(", ", (entry?.OtherPatterns ?? []).Concat(entry?.MoreIdentityFiles ?? []));
        var note = new TextBlock
        {
            Text = also.Length == 0 ? string.Empty : $"Also in this entry, kept as is: {also}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new StackPanel { Spacing = 8, MinWidth = 400, Children = { alias, hostName, user, identityFile, note, error } },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var newAlias = alias.Text.Trim();
            var duplicate = State.SshHosts.Any(other => !string.Equals(other.Alias, entry?.Alias, StringComparison.OrdinalIgnoreCase)
                && string.Equals(other.Alias, newAlias, StringComparison.OrdinalIgnoreCase));
            error.Text = newAlias.Length == 0 ? "Give the host an alias."
                : !SshConfigDocument.IsValidAlias(newAlias) ? "The alias is one word, without * ? ! # or quotes."
                : duplicate ? $"There is already a host '{newAlias}'."
                : string.Empty;
            args.Cancel = error.Text.Length > 0;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return new SshHostFields(alias.Text.Trim(), NullIfEmpty(hostName.Text), NullIfEmpty(user.Text), NullIfEmpty(identityFile.Text));
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
