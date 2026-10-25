using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>The configured apps with their Kill/Restart ticks, the restart buttons and app profiles.</summary>
public sealed partial class AppsPage : Page
{
    private readonly ObservableCollection<AppItem> _items = [];

    private static AppState State => App.State;

    public AppsPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        State.ConfigurationChanged += State_Changed;
        State.AppsChanged += State_Changed;
        State.BusyChanged += State_BusyChanged;
        Refresh();
        State_BusyChanged(null, EventArgs.Empty);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        State.ConfigurationChanged -= State_Changed;
        State.AppsChanged -= State_Changed;
        State.BusyChanged -= State_BusyChanged;
    }

    private void State_Changed(object? sender, EventArgs e) => Refresh();

    private void State_BusyChanged(object? sender, EventArgs e)
    {
        KillAndStart.IsEnabled = !State.Busy;
        RestartSelected.IsEnabled = !State.Busy;
    }

    private void Refresh()
    {
        _items.Clear();
        foreach (var app in State.VisibleApps)
        {
            _items.Add(new AppItem(app));
        }

        BuildProfileMenu();
    }

    private void Kill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AppItem item } box)
        {
            item.Kill = box.IsChecked == true;
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AppItem item } box)
        {
            item.Restart = box.IsChecked == true;
        }
    }

    private async void KillAndStart_Click(object sender, RoutedEventArgs e) =>
        await State.RunBusyAsync(() => AppController.KillAndStartAsync(State.VisibleApps, stopOnlySelected: false, State.LogMessage));

    private async void RestartSelected_Click(object sender, RoutedEventArgs e) =>
        await State.RunBusyAsync(() => AppController.KillAndStartAsync(State.VisibleApps, stopOnlySelected: true, State.LogMessage));

    private void EditConfig_Click(object sender, RoutedEventArgs e) =>
        ConfigFileOpener.Open(State.ConfigPath, State.LogMessage);

    // --------------------------------------------------------------- profiles

    private Profile? SelectedProfile() => ProfileOperations.Find(State.Config.Profiles, State.SelectedProfileName);

    private void BuildProfileMenu()
    {
        if (SelectedProfile() is null)
        {
            State.SelectedProfileName = State.Config.Profiles.FirstOrDefault()?.Name;
        }

        ProfileButton.Content = State.SelectedProfileName is null ? "Profile" : $"Profile: {State.SelectedProfileName}";
        ProfileMenu.Items.Clear();

        foreach (var profile in State.Config.Profiles)
        {
            var choice = new RadioMenuFlyoutItem
            {
                Text = profile.Name,
                GroupName = "Profile",
                IsChecked = string.Equals(profile.Name, State.SelectedProfileName, StringComparison.OrdinalIgnoreCase)
            };
            choice.Click += (_, _) =>
            {
                State.SelectedProfileName = profile.Name;
                BuildProfileMenu();
            };
            ProfileMenu.Items.Add(choice);
        }

        if (State.Config.Profiles.Count > 0)
        {
            ProfileMenu.Items.Add(new MenuFlyoutSeparator());
        }

        ProfileMenu.Items.Add(Command("Load", "Apply this profile's ticks to the list.", LoadProfile));
        ProfileMenu.Items.Add(Command("Save As…", "Store the current ticks as a new profile.", SaveProfileAs));
        ProfileMenu.Items.Add(Command("Update", "Overwrite the selected profile with the current ticks.", UpdateProfile));
        ProfileMenu.Items.Add(Command("Delete", "Remove the selected profile.", DeleteProfile));
    }

    private static MenuFlyoutItem Command(string text, string tooltip, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        ToolTipService.SetToolTip(item, tooltip);
        item.Click += (_, _) => onClick();
        return item;
    }

    private void LoadProfile()
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            State.LogMessage("No profile selected.");
            return;
        }

        State.ApplyProfile(profile);
    }

    private async void SaveProfileAs()
    {
        var name = await Dialogs.PromptAsync(XamlRoot, "Save profile as", "Profile name:", State.SelectedProfileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        var existing = ProfileOperations.Find(State.Config.Profiles, name);
        if (existing is not null
            && !await Dialogs.ConfirmAsync(XamlRoot, "Save profile as", $"Profile '{existing.Name}' already exists. Overwrite it?"))
        {
            return;
        }

        var profile = ProfileOperations.Upsert(State.Config.Profiles, name, ProfileOperations.Capture(State.VisibleApps));
        State.LogMessage(existing is null ? $"Created profile '{name}'." : $"Overwrote profile '{profile.Name}'.");

        State.SelectedProfileName = profile.Name;
        State.Save();
    }

    private void UpdateProfile()
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            State.LogMessage("No profile selected.");
            return;
        }

        profile.Entries = ProfileOperations.Capture(State.VisibleApps);
        State.Save();
        State.LogMessage($"Updated profile '{profile.Name}'.");
    }

    private async void DeleteProfile()
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            State.LogMessage("No profile selected.");
            return;
        }

        if (!await Dialogs.ConfirmAsync(XamlRoot, "Delete profile", $"Delete profile '{profile.Name}'?"))
        {
            return;
        }

        State.Config.Profiles.Remove(profile);
        State.SelectedProfileName = null;
        State.Save();
        State.LogMessage($"Deleted profile '{profile.Name}'.");
    }
}
