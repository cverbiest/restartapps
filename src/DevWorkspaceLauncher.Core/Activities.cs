using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>What an activity does to one of its workspaces.</summary>
public enum WorkspaceAction
{
    /// <summary>Open the workspace; VS Code focuses it if it is already open.</summary>
    Open,

    /// <summary>Ask the workspace's window to close, never forcing it.</summary>
    Close
}

public sealed class WorkspaceActionConverter() : CaseInsensitiveEnumConverter<WorkspaceAction>("Action");

/// <summary>
/// A named one-click button that acts on a set of actions, workspaces, RDP sessions and SSH hosts.
/// Anything it does not list is left alone.
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

    /// <summary>Never null: <c>"Actions": null</c> in the file reads as an empty list.</summary>
    public List<ActivityAction> Actions
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>The actions as files before actions had them; <see cref="ActionMigration"/> moves them into <see cref="Actions"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ActivityAction>? Apps { get; set; }

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

    /// <summary>The SSH hosts to connect to. Never null: <c>"SshHosts": null</c> reads as an empty list.</summary>
    public List<ActivitySshHost> SshHosts
    {
        get;
        set => field = value ?? [];
    } = [];
}

public sealed class ActivitySshHost
{
    /// <summary>The alias of the host in <c>~/.ssh/config</c>, matched case-insensitively.</summary>
    public required string Name { get; set; }
}

public sealed class ActivityRdpSession
{
    /// <summary>The <see cref="RdpSession.Name"/> of the session, matched case-insensitively.</summary>
    public required string Name { get; set; }
}

public sealed class ActivityAction
{
    /// <summary>The <see cref="ActionConfiguration.Name"/> of the action, matched case-insensitively.</summary>
    public required string Name { get; set; }

    /// <summary>What is done with it; one of the action's <see cref="ActionConfiguration.Verbs"/>.</summary>
    [JsonConverter(typeof(ActionVerbConverter))]
    public ActionVerb Action { get; set; } = ActionVerb.Start;
}

public sealed class ActivityWorkspace
{
    /// <summary>The workspace URI as VS Code stores it. Need not be on the preferred list.</summary>
    public required string Uri { get; set; }

    [JsonConverter(typeof(WorkspaceActionConverter))]
    public WorkspaceAction Action { get; set; } = WorkspaceAction.Open;
}
