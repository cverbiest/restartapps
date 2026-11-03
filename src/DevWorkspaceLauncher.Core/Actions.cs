using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>What kind of action an entry in <see cref="ConfigurationFile.Actions"/> is.</summary>
public enum ActionKind
{
    /// <summary>A program the launcher starts and stops: <see cref="ApplicationAction"/>.</summary>
    Application,

    /// <summary>Something the launcher itself does: <see cref="BuiltInAction"/>.</summary>
    BuiltIn
}

/// <summary>The built-in actions. The names are stored in the file, so they never change.</summary>
public enum BuiltInActionId
{
    /// <summary>Moves every window on another monitor to the primary one; see <see cref="WindowMover"/>.</summary>
    MoveWindowsToMainDisplay
}

/// <summary>
/// What is done with an action, from an activity or a button. Which ones apply depends on the action;
/// see <see cref="ActionConfiguration.Verbs"/>.
/// </summary>
public enum ActionVerb
{
    /// <summary>Start the application, unless it is already running.</summary>
    Start,

    /// <summary>Stop the application by its <see cref="StopMethod"/>.</summary>
    Stop,

    /// <summary>Stop the application, then start it.</summary>
    Restart,

    /// <summary>Do what a built-in action does.</summary>
    Run
}

public sealed class ActionKindConverter() : CaseInsensitiveEnumConverter<ActionKind>("Kind");

public sealed class BuiltInActionIdConverter() : CaseInsensitiveEnumConverter<BuiltInActionId>("BuiltIn");

public sealed class ActionVerbConverter() : CaseInsensitiveEnumConverter<ActionVerb>("Action");

/// <summary>
/// One entry of <see cref="ConfigurationFile.Actions"/>: an application or a built-in action. The
/// file tells them apart by <c>Kind</c>; an entry without one is an application.
/// </summary>
[JsonConverter(typeof(ActionConfigurationConverter))]
public abstract class ActionConfiguration : IFavorite
{
    [JsonPropertyOrder(-1)]
    [JsonConverter(typeof(ActionKindConverter))]
    public ActionKind Kind => this is BuiltInAction ? ActionKind.BuiltIn : ActionKind.Application;

    public required string Name { get; set; }

    /// <summary>Shown on the start page and in the tray menu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Favorite { get; set; }

    /// <summary>Its place among the favorites; see <see cref="IFavorite.FavoriteOrder"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int FavoriteOrder { get; set; }

    /// <summary>What can be done with it, the default first: what an activity or a click does unless told otherwise.</summary>
    [JsonIgnore]
    public abstract IReadOnlyList<ActionVerb> Verbs { get; }
}

public sealed class ApplicationAction : ActionConfiguration
{
    /// <summary>Command line exactly as stored in the file, so it may still contain %VARIABLES%.</summary>
    public required string Commandline { get; set; }

    /// <summary>Whether "Kill &amp; Start Selected" stops this app. Absent from the file means yes.</summary>
    public bool Kill { get; set; } = true;

    /// <summary>Whether this app is started, and so whether "Restart Selected" covers it.</summary>
    public bool Restart { get; set; }

    /// <summary>Hidden from the grid and ignored by every action, but kept in the file.</summary>
    public bool Disabled { get; set; }

    [JsonConverter(typeof(StopMethodConverter))]
    public StopMethod StopMethod { get; set; } = StopMethod.Process;

    /// <summary>
    /// For <see cref="StopMethod.Window"/>: the AppUserModelID of the windows to close. Supports
    /// <c>*</c> and <c>?</c> wildcards. This is how a browser-hosted PWA is targeted without
    /// touching the browser process it shares with every other window.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowAumid { get; set; }

    [JsonIgnore]
    public string ExpandedCommandline => Environment.ExpandEnvironmentVariables(Commandline);

    [JsonIgnore]
    public bool CanStop => StopMethod != StopMethod.None;

    public override IReadOnlyList<ActionVerb> Verbs => [ActionVerb.Start, ActionVerb.Stop, ActionVerb.Restart];
}

/// <summary>An action the launcher itself carries out; <see cref="BuiltInActions"/> lists them.</summary>
public sealed class BuiltInAction : ActionConfiguration
{
    [JsonConverter(typeof(BuiltInActionIdConverter))]
    public BuiltInActionId BuiltIn { get; set; }

    [JsonIgnore]
    public string Description => BuiltInActions.Describe(BuiltIn).Description;

    public override IReadOnlyList<ActionVerb> Verbs => BuiltInActions.Describe(BuiltIn).Verbs;
}

/// <summary>A built-in action as the launcher offers it.</summary>
public sealed record BuiltInActionInfo(BuiltInActionId Id, string Name, string Description, IReadOnlyList<ActionVerb> Verbs);

/// <summary>
/// The built-in actions. Each is always on offer, but the file holds one only while it says something:
/// pinned, or used by an activity.
/// </summary>
public static class BuiltInActions
{
    public static IReadOnlyList<BuiltInActionInfo> All { get; } =
    [
        new(BuiltInActionId.MoveWindowsToMainDisplay,
            "Move all windows to the main display",
            "Moves every window on another monitor to the main one, keeping its size and whether it is maximized.",
            [ActionVerb.Run])
    ];

    public static BuiltInActionInfo Describe(BuiltInActionId id) => All.First(info => info.Id == id);

    /// <summary>Adds the built-in actions the configuration does not have yet, after the others.</summary>
    public static void AddMissing(ConfigurationFile config)
    {
        foreach (var info in All)
        {
            if (!config.Actions.OfType<BuiltInAction>().Any(action => action.BuiltIn == info.Id))
            {
                config.Actions.Add(new BuiltInAction { Name = info.Name, BuiltIn = info.Id });
            }
        }
    }

    /// <summary>Whether <paramref name="action"/> belongs in the file: any application, a built-in one only while pinned or used.</summary>
    public static bool IsKept(ActionConfiguration action, ConfigurationFile config) =>
        action is not BuiltInAction
        || action.Favorite
        || config.Activities.Any(activity => activity.Actions.Any(item =>
            string.Equals(item.Name, action.Name, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>Reads an action as the kind its <c>Kind</c> names (case-insensitively; none means an application).</summary>
public sealed class ActionConfigurationConverter : JsonConverter<ActionConfiguration>
{
    public override ActionConfiguration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = ActionKind.Application;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(ActionConfiguration.Kind), StringComparison.OrdinalIgnoreCase))
            {
                kind = property.Value.Deserialize<ActionKind>(new JsonSerializerOptions { Converters = { new ActionKindConverter() } });
            }
        }

        return kind switch
        {
            ActionKind.BuiltIn => root.Deserialize<BuiltInAction>(options),
            _ => root.Deserialize<ApplicationAction>(options)
        };
    }

    public override void Write(Utf8JsonWriter writer, ActionConfiguration value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

/// <summary>
/// Moves the applications of a file from before actions into <see cref="ConfigurationFile.Actions"/>,
/// and an activity's apps into its actions. Done once; the caller keeps a copy of the file first.
/// </summary>
public static class ActionMigration
{
    public static bool IsNeeded(ConfigurationFile config) =>
        config.Apps is not null || config.Activities.Any(activity => activity.Apps is not null);

    /// <summary>Returns a line for the log about what was converted.</summary>
    public static List<string> Convert(ConfigurationFile config)
    {
        var messages = new List<string>();
        if (config.Apps is { } apps)
        {
            var added = apps.Where(app => !config.Actions.Any(action =>
                string.Equals(action.Name, app.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            config.Actions.InsertRange(0, added);
            messages.Add($"{added.Count} app(s) are now actions of kind Application.");
            messages.AddRange(apps.Except(added).Select(app => $"App '{app.Name}' was dropped: there is already an action with that name."));
            config.Apps = null;
        }

        foreach (var activity in config.Activities.Where(activity => activity.Apps is not null))
        {
            activity.Actions.InsertRange(0, activity.Apps!);
            activity.Apps = null;
        }

        return messages;
    }
}
