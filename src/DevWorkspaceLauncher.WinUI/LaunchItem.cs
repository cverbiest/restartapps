namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// Something that runs with one click - an activity, action, RDP session or SSH host - as the start page, the All
/// items and Activities pages and the tray menu show it.
/// </summary>
public sealed class LaunchItem : GridRow<LaunchItem>
{
    private const string PinnedGlyph = "";
    private const string UnpinnedGlyph = "";

    private Action _run;

    private LaunchItem(IFavorite target, string kind, string glyph, string tooltip, Action run)
    {
        Target = target;
        Kind = kind;
        Glyph = glyph;
        Tooltip = tooltip;
        _run = run;
    }

    private static AppState State => App.State;

    /// <summary>The item: an <see cref="Activity"/>, <see cref="ActionConfiguration"/>, <see cref="RdpSession"/> or <see cref="SshHost"/>.</summary>
    public IFavorite Target { get; private set; }

    public string Name => ItemActions.NameOf(Target);

    /// <summary>What sort of item this is, as the lists show it.</summary>
    public string Kind { get; private set; }

    public string Glyph { get; private set; }

    /// <summary>What running it does.</summary>
    public string Tooltip { get; private set; }

    /// <summary>Names are unique within a kind, not across kinds.</summary>
    public override string Key => $"{Kind}\n{Name}";

    protected override void CopyFrom(LaunchItem fresh)
    {
        Target = fresh.Target;
        Kind = fresh.Kind;
        Glyph = fresh.Glyph;
        Tooltip = fresh.Tooltip;
        _run = fresh._run;
    }

    /// <summary>On the start page and in the tray menu.</summary>
    public bool Favorite => Target.Favorite;

    public string FavoriteGlyph => PinGlyph(Favorite);

    public Microsoft.UI.Xaml.Media.Brush FavoriteBrush => PinBrush(Favorite);

    public string FavoriteTooltip => PinTooltip(Favorite);

    public void Run() => _run();

    /// <summary>Every launchable item, grouped by kind.</summary>
    public static IReadOnlyList<LaunchItem> All(ConfigurationFile config) =>
    [
        .. Activities(config),
        .. config.Actions.Where(action => action is not ApplicationAction { Disabled: true }).Select(action => new LaunchItem(
            action, ActionItem.KindOf(action), ActionItem.GlyphOf(action),
            action is BuiltInAction builtIn
                ? builtIn.Description
                : $"Start {action.Name}, or bring it to the front when it is running.",
            () => State.RunAction(action))),
        .. config.RdpSessions.Select(session => new LaunchItem(
            session, "RDP session", "",
            $"Connect to {session.Host}.",
            () => State.ConnectRdp(session))),
        .. State.SshHosts.Select(host => new LaunchItem(
            host, "SSH host", "",
            $"Connect to {host.Alias} with ssh.",
            () => State.ConnectSsh(host)))
    ];

    public static IEnumerable<LaunchItem> Activities(ConfigurationFile config) =>
        config.Activities.Select(activity => new LaunchItem(
            activity, "Activity", "",
            Describe(activity),
            () => _ = State.RunActivityAsync(activity)));

    /// <summary>The favorites in their start page order, which the tray menu follows too.</summary>
    public static IReadOnlyList<LaunchItem> Favorites(ConfigurationFile config) => FavoriteOrdering.Order(All(config), item => item.Target);

    /// <summary>The pin glyph for a favorite flag, for lists that bind the configuration objects directly.</summary>
    public static string PinGlyph(bool favorite) => favorite ? PinnedGlyph : UnpinnedGlyph;

    /// <summary>The pin button's tooltip (and screen reader name) for a favorite flag.</summary>
    public static string PinTooltip(bool favorite) => favorite
        ? "Pinned to the start page and the tray menu. Click to unpin."
        : "Pin to the start page and the tray menu.";

    /// <summary>Orange (the app icon's colour) for a favorite, the secondary text colour otherwise.</summary>
    public static Microsoft.UI.Xaml.Media.Brush PinBrush(bool favorite) => favorite
        ? FavoriteColor
        : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorTertiaryBrush"];

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FavoriteColor =
        new(Windows.UI.Color.FromArgb(0xFF, 0xF2, 0x71, 0x1C));

    private static string Describe(Activity activity)
    {
        var parts = new List<string>();
        if (activity.Actions.Count > 0)
        {
            parts.Add($"{activity.Actions.Count} action(s)");
        }

        if (activity.Workspaces.Count > 0)
        {
            parts.Add($"{activity.Workspaces.Count} workspace(s)");
        }

        if (activity.RdpSessions.Count > 0)
        {
            parts.Add($"{activity.RdpSessions.Count} RDP session(s)");
        }

        if (activity.SshHosts.Count > 0)
        {
            parts.Add($"{activity.SshHosts.Count} SSH host(s)");
        }

        return parts.Count == 0 ? $"'{activity.Name}' is empty." : $"Run '{activity.Name}': {string.Join(", ", parts)}.";
    }
}
