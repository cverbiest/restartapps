using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// The dialog that renames an activity and sets, per item, what it does - or takes the item out.
/// Items are added from the other tabs (Add to activity), not here.
/// </summary>
internal static class ActivityEditor
{
    private static AppState State => App.State;

    /// <summary>Shows the dialog; on OK writes the changes into <paramref name="activity"/> and returns true.</summary>
    public static async Task<bool> EditAsync(XamlRoot root, Activity activity)
    {
        var name = new TextBox { Header = "Name", Text = activity.Name };
        var rows = new StackPanel { Spacing = 4 };
        var apply = new List<Action>();

        foreach (var app in activity.Apps)
        {
            var action = Choice(Enum.GetNames<AppAction>(), app.Action.ToString());
            AddRow("App", app.Name, action,
                keep: () => app.Action = Enum.Parse<AppAction>((string)action.SelectedItem),
                remove: () => activity.Apps.Remove(app));
        }

        foreach (var workspace in activity.Workspaces)
        {
            var action = Choice(Enum.GetNames<WorkspaceAction>(), workspace.Action.ToString());
            var favorite = State.Config.Workspaces.FirstOrDefault(candidate =>
                string.Equals(candidate.Uri, workspace.Uri, StringComparison.OrdinalIgnoreCase));
            AddRow("Workspace", VsCodeUri.Title(favorite?.Name ?? VsCodeUri.Describe(workspace.Uri).DisplayName, workspace.Uri), action,
                keep: () => workspace.Action = Enum.Parse<WorkspaceAction>((string)action.SelectedItem),
                remove: () => activity.Workspaces.Remove(workspace));
        }

        foreach (var session in activity.RdpSessions)
        {
            AddRow("RDP session", session.Name, new TextBlock { Text = "Connect", VerticalAlignment = VerticalAlignment.Center },
                keep: () => { },
                remove: () => activity.RdpSessions.Remove(session));
        }

        var empty = new TextBlock
        {
            Text = "Empty. Select items on the All items, Apps, VS Code workspaces or RDP sessions tab and use Add to activity.",
            TextWrapping = TextWrapping.Wrap,
            Visibility = rows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed
        };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Edit activity",
            Content = new StackPanel
            {
                Spacing = 12,
                MinWidth = 520,
                Children =
                {
                    name,
                    new ScrollViewer { MaxHeight = 420, Content = new StackPanel { Children = { rows, empty } } },
                    error
                }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var newName = name.Text.Trim();
            var duplicate = State.Config.Activities.Any(other => !ReferenceEquals(other, activity)
                && string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase));
            error.Text = newName.Length == 0 ? "Give the activity a name."
                : duplicate ? $"There is already an activity named '{newName}'."
                : string.Empty;
            args.Cancel = error.Text.Length > 0;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        activity.Name = name.Text.Trim();
        foreach (var change in apply)
        {
            change();
        }

        return true;

        void AddRow(string kind, string itemName, FrameworkElement action, Action keep, Action remove)
        {
            var removed = false;
            var row = new Grid { ColumnSpacing = 8, MinHeight = 36 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var kindText = new TextBlock
            {
                Text = kind,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            var nameText = new TextBlock { Text = itemName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var removeButton = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8) };
            ToolTipService.SetToolTip(removeButton, "Take this item out of the activity.");
            removeButton.Click += (_, _) =>
            {
                removed = true;
                row.Visibility = Visibility.Collapsed;
            };

            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(action, 2);
            Grid.SetColumn(removeButton, 3);
            row.Children.Add(kindText);
            row.Children.Add(nameText);
            row.Children.Add(action);
            row.Children.Add(removeButton);
            rows.Children.Add(row);

            apply.Add(() =>
            {
                if (removed)
                {
                    remove();
                }
                else
                {
                    keep();
                }
            });
        }
    }

    private static ComboBox Choice(string[] values, string current) =>
        new() { ItemsSource = values, SelectedItem = current, HorizontalAlignment = HorizontalAlignment.Stretch };
}
