using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// What every grid offers on its selected rows - quick search, delete, add to activity - for whatever
/// the rows are: an <see cref="Activity"/>, <see cref="AppConfiguration"/>, <see cref="RdpSession"/>
/// or <see cref="WorkspaceRow"/>.
/// </summary>
internal static class ItemActions
{
    private static AppState State => App.State;

    /// <summary>Whether a row matches the quick search: every word appears in one of its fields.</summary>
    public static bool Matches(string query, params string?[] fields) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
            fields.Any(field => field?.Contains(word, StringComparison.CurrentCultureIgnoreCase) == true));

    public static string NameOf(object item) => item switch
    {
        Activity activity => activity.Name,
        AppConfiguration app => app.Name,
        RdpSession session => session.Name,
        WorkspaceRow row => row.Name,
        _ => item.ToString() ?? string.Empty
    };

    /// <summary>
    /// Asks, then deletes the items. Apps and RDP sessions also leave the activities that use them; a
    /// workspace leaves the preferred list (VS Code's own recent list is not ours to change).
    /// </summary>
    public static async Task DeleteAsync(XamlRoot root, IReadOnlyList<object> items)
    {
        if (items.Count == 0)
        {
            State.LogMessage("Nothing selected.");
            return;
        }

        var workspaces = items.OfType<WorkspaceRow>().ToList();
        if (workspaces.Count > 0 && workspaces.All(row => !row.Preferred))
        {
            State.LogMessage("Only preferred workspaces can be removed; the others are VS Code's recent list.");
            return;
        }

        var names = string.Join(", ", items.Take(10).Select(item => $"'{NameOf(item)}'"))
            + (items.Count > 10 ? $" and {items.Count - 10} more" : string.Empty);
        var usedIn = items.Count(item => item is AppConfiguration or RdpSession) > 0
            ? " Apps and RDP sessions are also taken out of the activities that use them."
            : string.Empty;
        if (!await Dialogs.ConfirmAsync(root, "Delete", $"Delete {names}?{usedIn}"))
        {
            return;
        }

        var config = State.Config;
        foreach (var item in items)
        {
            switch (item)
            {
                case Activity activity:
                    config.Activities.Remove(activity);
                    break;

                case AppConfiguration app:
                    config.Apps.Remove(app);
                    ActivityOperations.RemoveApp(config, app.Name);
                    break;

                case RdpSession session:
                    config.RdpSessions.Remove(session);
                    ActivityOperations.RemoveRdpSession(config, session.Name);
                    break;

                case WorkspaceRow { Preferred: true } row:
                    config.Workspaces.RemoveAll(workspace => string.Equals(workspace.Uri, row.Uri, StringComparison.OrdinalIgnoreCase));
                    break;
            }
        }

        State.Save();
        State.LogMessage($"Deleted {names}.");
    }

    /// <summary>
    /// Fills an "Add to activity" menu each time it opens: the activities, then New activity. The
    /// chosen activity gets the rows <paramref name="selected"/> returns at that moment.
    /// </summary>
    public static void AttachActivityMenu(MenuFlyout menu, Func<XamlRoot> root, Func<IReadOnlyList<object>> selected)
    {
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var activity in State.Config.Activities)
            {
                var item = new MenuFlyoutItem { Text = activity.Name };
                item.Click += (_, _) => AddTo(activity, selected());
                menu.Items.Add(item);
            }

            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var create = new MenuFlyoutItem { Text = "New activity…", Icon = new FontIcon { Glyph = "" } };
            create.Click += async (_, _) =>
            {
                var items = selected();
                if (items.Count == 0)
                {
                    State.LogMessage("Nothing selected.");
                    return;
                }

                var name = await Dialogs.PromptAsync(root(), "New activity", "Activity name:", string.Empty);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                var activity = new Activity { Name = ActivityOperations.UniqueName(State.Config.Activities, name.Trim()) };
                State.Config.Activities.Add(activity);
                AddTo(activity, items);
            };
            menu.Items.Add(create);
        };
    }

    private static void AddTo(Activity activity, IReadOnlyList<object> items)
    {
        if (items.Count == 0)
        {
            State.LogMessage("Nothing selected.");
            return;
        }

        var added = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case AppConfiguration app when ActivityOperations.AddApp(activity, app.Name):
                case RdpSession session when ActivityOperations.AddRdpSession(activity, session.Name):
                case WorkspaceRow row when ActivityOperations.AddWorkspace(activity, row.Uri):
                    added++;
                    break;

                case Activity:
                    State.LogMessage($"'{NameOf(item)}' is an activity; activities inside activities are not supported yet.");
                    break;
            }
        }

        State.Save();
        var already = items.Count(item => item is not Activity) - added;
        var suffix = already > 0 ? $" ({already} already in it)" : string.Empty;
        State.LogMessage($"Added {added} item(s) to activity '{activity.Name}'.{suffix}");
    }
}
