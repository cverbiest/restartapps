using System.Text.Json;

namespace DevWorkspaceLauncher;

/// <summary>Where a window was: its restored bounds in physical pixels, and whether it was maximized.</summary>
public sealed record WindowPlacement(int Left, int Top, int Width, int Height, bool Maximized = false)
{
    /// <summary>How much of the title bar must be on a working area for the window to count as reachable.</summary>
    private const int MinimumVisibleWidth = 100;
    private const int TitleBarHeight = 32;

    public PixelRect Bounds => new(Left, Top, Left + Width, Top + Height);

    /// <summary>
    /// The placement itself when enough of its title bar is on one of <paramref name="monitors"/> to drag
    /// it; otherwise the same size, shrunk to fit if needed, centred on the primary monitor.
    /// </summary>
    public WindowPlacement Fit(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0 || monitors.Any(monitor => IsReachableOn(monitor.WorkingArea)))
        {
            return this;
        }

        var work = (monitors.FirstOrDefault(monitor => monitor.Primary) ?? monitors[0]).WorkingArea;
        var width = Math.Min(Width, work.Width);
        var height = Math.Min(Height, work.Height);
        return this with
        {
            Left = work.Left + (work.Width - width) / 2,
            Top = work.Top + (work.Height - height) / 2,
            Width = width,
            Height = height
        };
    }

    private bool IsReachableOn(PixelRect work)
    {
        var visibleWidth = Math.Min(Left + Width, work.Right) - Math.Max(Left, work.Left);
        var titleBarOn = Top >= work.Top && Top + TitleBarHeight <= work.Bottom;
        return visibleWidth >= Math.Min(MinimumVisibleWidth, Width) && titleBarOn;
    }
}

/// <summary>
/// The main window's last placement. Kept in its own file in %LOCALAPPDATA%, not in the configuration:
/// it changes whenever the window moves, and saving the configuration that often would reload it.
/// </summary>
public static class WindowPlacementStore
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppIdentity.CodeName,
        "window.json");

    /// <summary>The saved placement, or null when there is none or it cannot be read.</summary>
    public static WindowPlacement? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(WindowPlacement placement)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(placement));
    }
}
