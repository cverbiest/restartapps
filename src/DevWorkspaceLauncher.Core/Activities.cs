using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>What an activity does to one of its apps.</summary>
public enum AppAction
{
    /// <summary>Start the app, unless it is already running.</summary>
    Start,

    /// <summary>Stop the app by its <see cref="StopMethod"/>.</summary>
    Stop,

    /// <summary>Stop the app, then start it.</summary>
    Restart
}

/// <summary>What an activity does to one of its workspaces.</summary>
public enum WorkspaceAction
{
    /// <summary>Open the workspace; VS Code focuses it if it is already open.</summary>
    Open,

    /// <summary>Ask the workspace's window to close, never forcing it.</summary>
    Close
}

public sealed class AppActionConverter() : CaseInsensitiveEnumConverter<AppAction>("Action");

public sealed class WorkspaceActionConverter() : CaseInsensitiveEnumConverter<WorkspaceAction>("Action");

/// <summary>
/// A named one-click button that acts on a set of apps, workspaces and RDP sessions. Anything it does
/// not list is left alone.
/// </summary>
public sealed class Activity : IFavorite
{
    public required string Name { get; set; }

    /// <summary>Shown on the start page and in the tray menu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Favorite { get; set; }

    /// <summary>Its place among the favorites; see <see cref="IFavorite.FavoriteOrder"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int FavoriteOrder { get; set; }

    /// <summary>Never null: <c>"Apps": null</c> in the file reads as an empty list.</summary>
    public List<ActivityApp> Apps
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>Never null: <c>"Workspaces": null</c> in the file reads as an empty list.</summary>
    public List<ActivityWorkspace> Workspaces
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>The RDP sessions to connect to. Never null: <c>"RdpSessions": null</c> reads as an empty list.</summary>
    public List<ActivityRdpSession> RdpSessions
    {
        get;
        set => field = value ?? [];
    } = [];
}

public sealed class ActivityRdpSession
{
    /// <summary>The <see cref="RdpSession.Name"/> of the session, matched case-insensitively.</summary>
    public required string Name { get; set; }
}

public sealed class ActivityApp
{
    /// <summary>The <see cref="AppConfiguration.Name"/> of the app, matched case-insensitively.</summary>
    public required string Name { get; set; }

    [JsonConverter(typeof(AppActionConverter))]
    public AppAction Action { get; set; } = AppAction.Start;
}

public sealed class ActivityWorkspace
{
    /// <summary>The workspace URI as VS Code stores it. Need not be on the preferred list.</summary>
    public required string Uri { get; set; }

    [JsonConverter(typeof(WorkspaceActionConverter))]
    public WorkspaceAction Action { get; set; } = WorkspaceAction.Open;
}
