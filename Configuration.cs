using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestartApps;

/// <summary>How an application is stopped.</summary>
internal enum StopMethod
{
    /// <summary>Kill every process whose executable name matches the command line.</summary>
    Process,

    /// <summary>Close the top-level windows matching <see cref="AppConfiguration.WindowAumid"/>.</summary>
    Window,

    /// <summary>Never stop this application.</summary>
    None
}

/// <summary>Case-insensitive so the configuration file can say "window" or "Window".</summary>
internal sealed class StopMethodConverter : JsonConverter<StopMethod>
{
    public override StopMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (Enum.TryParse<StopMethod>(value, ignoreCase: true, out var method))
        {
            return method;
        }

        throw new JsonException($"Unknown StopMethod '{value}'. Use Process, Window or None.");
    }

    public override void Write(Utf8JsonWriter writer, StopMethod value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

internal sealed class AppConfiguration
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
internal sealed class Profile
{
    public required string Name { get; set; }
    public List<ProfileEntry> Entries { get; set; } = [];
}

internal sealed class ProfileEntry
{
    public required string Name { get; set; }
    public bool Kill { get; set; }
    public bool Restart { get; set; }
}

internal sealed class ConfigurationFile
{
    public List<AppConfiguration> Apps { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
}

internal sealed record ConfigurationSnapshot(ConfigurationFile Configuration, string Json);

internal static class AppConfigurationStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = FindConfigFile();

    public static ConfigurationSnapshot Load()
    {
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {FilePath}", FilePath);
        }

        var json = File.ReadAllText(FilePath);
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

    private static ConfigurationFile Parse(string json)
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
        var json = JsonSerializer.Serialize(configuration, WriteOptions);
        File.WriteAllText(FilePath, json);
        return json;
    }

    private static string FindConfigFile()
    {
        var executableConfig = Path.Combine(AppContext.BaseDirectory, "restartapps.json");
        if (File.Exists(executableConfig))
        {
            return executableConfig;
        }

        var currentDirectoryConfig = Path.Combine(Environment.CurrentDirectory, "restartapps.json");
        return File.Exists(currentDirectoryConfig) ? currentDirectoryConfig : executableConfig;
    }
}
