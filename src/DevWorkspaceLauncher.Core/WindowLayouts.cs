using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>How a recorded window was shown.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecordedWindowState>))]
public enum RecordedWindowState
{
    Normal,
    Maximized,
    Minimized
}

/// <summary>
/// One window of a <see cref="WindowLayout"/>: which program and title it had, and its restored bounds
/// as <c>GetWindowPlacement</c> gives them (workspace coordinates, physical pixels). A minimized window
/// keeps whether it comes back maximized.
/// </summary>
public sealed record RecordedWindow(
    string Program,
    string Title,
    int Left,
    int Top,
    int Right,
    int Bottom,
    RecordedWindowState State = RecordedWindowState.Normal,
    bool RestoreToMaximized = false)
{
    [JsonIgnore]
    public WindowIdentity Identity => new(Program, Title);
}

/// <summary>What a window is matched by: the file name of its program and its title.</summary>
public readonly record struct WindowIdentity(string Program, string Title);

/// <summary>The positions of the open windows, recorded for one set of connected monitors.</summary>
public sealed class WindowLayout
{
    /// <summary>The monitors it was recorded for; see <see cref="MonitorSetup.KeyOf"/>.</summary>
    public required string Monitors { get; set; }

    /// <summary>The monitors in words, for the Settings page.</summary>
    public required string Description { get; set; }

    public DateTime Recorded { get; set; }

    public List<RecordedWindow> Windows { get; set; } = [];
}

/// <summary>Tells sets of connected monitors apart.</summary>
public static class MonitorSetup
{
    /// <summary>
    /// The same for the same monitors in the same arrangement: each monitor's bounds, left to right,
    /// with the primary one marked. Windows keeps an arrangement per set of monitors, so docking at
    /// the same desk gives the same key.
    /// </summary>
    public static string KeyOf(IEnumerable<MonitorInfo> monitors) =>
        string.Join(";", Ordered(monitors).Select(monitor =>
            $"{monitor.Bounds.Left},{monitor.Bounds.Top},{monitor.Bounds.Width}x{monitor.Bounds.Height}{(monitor.Primary ? "*" : "")}"));

    /// <summary>For example "3 monitors: 2560×1440, 1920×1200 (main), 2560×1440", left to right.</summary>
    public static string Describe(IEnumerable<MonitorInfo> monitors)
    {
        var ordered = Ordered(monitors);
        var sizes = ordered.Select(monitor =>
            $"{monitor.Bounds.Width}×{monitor.Bounds.Height}{(monitor.Primary && ordered.Count > 1 ? " (main)" : "")}");
        return $"{ordered.Count} monitor{(ordered.Count == 1 ? "" : "s")}: {string.Join(", ", sizes)}";
    }

    private static List<MonitorInfo> Ordered(IEnumerable<MonitorInfo> monitors) =>
        monitors.OrderBy(monitor => monitor.Bounds.Left).ThenBy(monitor => monitor.Bounds.Top).ToList();
}

/// <summary>Pairs recorded windows with the windows open now.</summary>
public static class WindowMatcher
{
    /// <summary>
    /// Pairs each recorded window with at most one open window, and each open window with at most one
    /// recorded window: first those with the same program and title, then the rest of each program in
    /// the order given (front to back). Program names compare without case; titles exactly.
    /// </summary>
    public static IReadOnlyList<(int Recorded, int Open)> Match(IReadOnlyList<WindowIdentity> recorded, IReadOnlyList<WindowIdentity> open)
    {
        var pairs = new List<(int Recorded, int Open)>();
        var recordedLeft = Enumerable.Range(0, recorded.Count).ToList();
        var openLeft = Enumerable.Range(0, open.Count).ToList();

        foreach (var r in recordedLeft.ToList())
        {
            var o = openLeft.FindIndex(o => SameProgram(recorded[r], open[o]) && recorded[r].Title == open[o].Title);
            if (o >= 0)
            {
                pairs.Add((r, openLeft[o]));
                openLeft.RemoveAt(o);
                recordedLeft.Remove(r);
            }
        }

        foreach (var r in recordedLeft)
        {
            var o = openLeft.FindIndex(o => SameProgram(recorded[r], open[o]));
            if (o >= 0)
            {
                pairs.Add((r, openLeft[o]));
                openLeft.RemoveAt(o);
            }
        }

        return pairs.OrderBy(pair => pair.Recorded).ToList();
    }

    private static bool SameProgram(WindowIdentity a, WindowIdentity b) =>
        string.Equals(a.Program, b.Program, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The recorded window layouts, one per set of monitors. Kept in %LOCALAPPDATA% next to the main
/// window's placement: they belong to this PC's monitors.
/// </summary>
public static class WindowLayoutStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppIdentity.CodeName,
        "window-layouts.json");

    /// <summary>The recorded layouts; none when there is no file or it cannot be read.</summary>
    public static List<WindowLayout> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<WindowLayout>>(File.ReadAllText(FilePath), Options) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(List<WindowLayout> layouts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(layouts, Options));
    }

    /// <summary>Adds <paramref name="layout"/>, replacing the one recorded for the same monitors.</summary>
    public static void Put(List<WindowLayout> layouts, WindowLayout layout)
    {
        var index = layouts.FindIndex(existing => existing.Monitors == layout.Monitors);
        if (index >= 0)
        {
            layouts[index] = layout;
        }
        else
        {
            layouts.Add(layout);
        }
    }
}
