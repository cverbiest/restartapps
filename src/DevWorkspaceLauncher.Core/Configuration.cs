using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>How an application is stopped.</summary>
public enum StopMethod
{
    /// <summary>Kill every process whose executable name matches the command line.</summary>
    Process,

    /// <summary>Close the top-level windows matching <see cref="AppConfiguration.WindowAumid"/>.</summary>
    Window,

    /// <summary>Never stop this application.</summary>
    None
}

/// <summary>
/// Reads an enum case-insensitively, so the configuration file can say "window" or "Window", and names
/// the valid values when a value is unknown.
/// </summary>
public abstract class CaseInsensitiveEnumConverter<TEnum>(string propertyName) : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        var names = Enum.GetNames<TEnum>();
        var valid = string.Join(", ", names[..^1]) + " or " + names[^1];
        throw new JsonException($"Unknown {propertyName} '{value}'. Use {valid}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public sealed class StopMethodConverter() : CaseInsensitiveEnumConverter<StopMethod>("StopMethod");

public sealed class AppConfiguration
{
    public required string Name { get; set; }

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
}

/// <summary>A named set of Kill/Restart ticks.</summary>
public sealed class Profile
{
    public required string Name { get; set; }
    public List<ProfileEntry> Entries { get; set; } = [];
}

public sealed class ProfileEntry
{
    public required string Name { get; set; }
    public bool Kill { get; set; }
    public bool Restart { get; set; }
}

/// <summary>A VS Code workspace or folder kept on the Workspaces tab.</summary>
public sealed class WorkspaceFavorite
{
    /// <summary>Shown in the grid. Seeded from the workspace's own name and editable in the file.</summary>
    public required string Name { get; set; }

    /// <summary>The URI as VS Code stores it, which is what identifies and launches the workspace.</summary>
    public required string Uri { get; set; }

    /// <summary>
    /// Which window belongs to this workspace, with <c>*</c> and <c>?</c> wildcards. Only needed when
    /// the name alone is ambiguous - two workspaces called "develop", say - because closing refuses to
    /// guess between windows. Absent means match on the workspace name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowTitle { get; set; }
}

/// <summary>A named set of workspaces to open together. Opening only: profiles never close a window.</summary>
public sealed class WorkspaceProfile
{
    public required string Name { get; set; }

    /// <summary>The URIs to open, in the order they are opened.</summary>
    public List<string> Workspaces { get; set; } = [];
}

public sealed class ConfigurationFile
{
    public List<AppConfiguration> Apps { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>The preferred VS Code workspaces, in the order they are shown.</summary>
    public List<WorkspaceFavorite> Workspaces { get; set; } = [];

    /// <summary>Named sets of workspaces to open together.</summary>
    public List<WorkspaceProfile> WorkspaceProfiles { get; set; } = [];

    /// <summary>One-click buttons that start and stop a set of linked apps and workspaces.</summary>
    public List<Activity> Activities { get; set; } = [];

    /// <summary>Overrides where VS Code is looked for. Absent means the usual install locations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VsCodeExecutable { get; set; }
}

public sealed record ConfigurationSnapshot(ConfigurationFile Configuration, string Json);

public static class AppConfigurationStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly ConfigurationLocation Location = ConfigurationLocator.ResolveForCurrentProcess();

    public static string FilePath => Location.Path;

    /// <summary>
    /// Makes sure <see cref="FilePath"/> exists, migrating the pre-rename file or starting an empty one.
    /// Returns a line for the log when it did either, null when the file was already there.
    /// </summary>
    public static string? EnsureExists() => EnsureExists(Location);

    internal static string? EnsureExists(ConfigurationLocation location)
    {
        if (File.Exists(location.Path))
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(location.Path)!);

        if (location.MigrateFrom is not null)
        {
            File.Copy(location.MigrateFrom, location.Path, overwrite: false);
            return $"Migrated configuration from {location.MigrateFrom} to {location.Path}.";
        }

        File.WriteAllText(location.Path, ToJson(new ConfigurationFile()));
        return $"Created a new configuration at {location.Path}.";
    }

    public static ConfigurationSnapshot Load() => Load(FilePath);

    public static ConfigurationSnapshot Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var configuration = Parse(json);

        foreach (var app in configuration.Apps)
        {
            // An app that can never be stopped must not show a ticked Kill box.
            if (!app.CanStop)
            {
                app.Kill = false;
            }
        }

        return new ConfigurationSnapshot(configuration, json);
    }

    public static ConfigurationFile Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        // The original format was a bare array of applications; keep reading it.
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var apps = JsonSerializer.Deserialize<List<AppConfiguration>>(json, ReadOptions) ?? [];
            return new ConfigurationFile { Apps = apps };
        }

        return JsonSerializer.Deserialize<ConfigurationFile>(json, ReadOptions)
            ?? throw new InvalidOperationException($"Configuration file is empty: {FilePath}");
    }

    /// <summary>Writes the configuration back to disk and returns the JSON that was written.</summary>
    public static string Save(ConfigurationFile configuration)
    {
        var json = ToJson(configuration);
        File.WriteAllText(FilePath, json);
        return json;
    }

    public static string ToJson(ConfigurationFile configuration)
    {
        return JsonSerializer.Serialize(configuration, WriteOptions);
    }
}
