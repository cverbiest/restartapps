using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DevWorkspaceLauncher;

/// <summary>A rectangle in physical pixels, edges inclusive of left/top and exclusive of right/bottom.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>One display: its whole area and its working area (without the taskbar).</summary>
public sealed record MonitorInfo(PixelRect Bounds, PixelRect WorkingArea, bool Primary, int Dpi);

/// <summary>The size of a window's frame around its client area, in physical pixels.</summary>
public readonly record struct WindowChrome(int Horizontal, int Caption, int Bottom)
{
    /// <summary>Left plus right frame.</summary>
    public int ExtraWidth => 2 * Horizontal;

    /// <summary>Caption plus bottom frame.</summary>
    public int ExtraHeight => Caption + Bottom;
}

/// <summary>Where the session window goes and how big its remote desktop is.</summary>
public sealed record RdpGeometry(int ScreenModeId, int DesktopWidth, int DesktopHeight, PixelRect Window);

public static class RdpLayout
{
    /// <summary>The monitor <paramref name="number"/> (1-based) means; past the last, or none, is the primary one.</summary>
    public static MonitorInfo PickMonitor(IReadOnlyList<MonitorInfo> monitors, int? number)
    {
        if (number is { } n && n >= 1 && n <= monitors.Count)
        {
            return monitors[n - 1];
        }

        return monitors.FirstOrDefault(monitor => monitor.Primary) ?? monitors[0];
    }

    /// <summary>
    /// Computes the window and remote desktop size for <paramref name="display"/> on
    /// <paramref name="monitor"/>. Window sizes include the frame, so the client area - the remote
    /// desktop - is the window less <paramref name="chrome"/>.
    /// </summary>
    public static RdpGeometry Compute(RdpDisplay display, MonitorInfo monitor, WindowChrome chrome)
    {
        var area = monitor.WorkingArea;
        switch (display.Mode ?? RdpDisplayMode.FullScreen)
        {
            case RdpDisplayMode.FullScreen:
                return new RdpGeometry(2, monitor.Bounds.Width, monitor.Bounds.Height, monitor.Bounds);

            case RdpDisplayMode.Max:
                return Windowed(area, chrome);

            case RdpDisplayMode.Fit:
                var fit = display.Fit ?? RdpCommandLine.DefaultFit;
                var window = fit <= 100
                    ? Centered(area, area.Width * fit / 100, area.Height * fit / 100)
                    : new PixelRect(area.Left + fit, area.Top + fit, area.Right - fit, area.Bottom - fit);
                return Windowed(window, chrome);

            case RdpDisplayMode.Size:
                var width = display.Width ?? area.Width - chrome.ExtraWidth;
                var height = display.Height ?? area.Height - chrome.ExtraHeight;
                var placed = Centered(area, width + chrome.ExtraWidth, height + chrome.ExtraHeight);
                return new RdpGeometry(1, width, height, placed);

            default:
                throw new ArgumentOutOfRangeException(nameof(display), display.Mode, "Unknown display mode.");
        }
    }

    private static RdpGeometry Windowed(PixelRect window, WindowChrome chrome) =>
        new(1, Math.Max(200, window.Width - chrome.ExtraWidth), Math.Max(200, window.Height - chrome.ExtraHeight), window);

    /// <summary>A window of the given size centred in <paramref name="area"/>, its top-left kept inside it.</summary>
    private static PixelRect Centered(PixelRect area, int width, int height)
    {
        var left = area.Left + Math.Max(0, (area.Width - width) / 2);
        var top = area.Top + Math.Max(0, (area.Height - height) / 2);
        return new PixelRect(left, top, left + width, top + height);
    }
}

/// <summary>Builds the text of the <c>.rdp</c> file mstsc is started with.</summary>
public static class RdpFileWriter
{
    /// <summary>
    /// The base file's lines, then the raw options, then what the launcher sets itself - later lines win
    /// by key. Every setting the launcher depends on is written explicitly, so nothing mstsc remembered
    /// from an earlier session leaks in. The password is never written.
    /// </summary>
    public static string Build(RdpSettings settings, RdpGeometry geometry, string? baseFileText = null)
    {
        var lines = new List<(string Key, string Line)>();

        void Set(string line)
        {
            var key = KeyOf(line);
            if (key is null)
            {
                return;
            }

            lines.RemoveAll(existing => string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase));
            lines.Add((key, line));
        }

        if (baseFileText is not null)
        {
            foreach (var line in baseFileText.Split('\n'))
            {
                Set(line.Trim('\r', ' '));
            }
        }

        foreach (var option in settings.Options ?? [])
        {
            Set(option);
        }

        var (host, port) = RdpResolver.SplitHost(settings.Host!);
        Set($"full address:s:{(port == 3389 ? host : $"{host}:{port}")}");
        Set($"screen mode id:i:{geometry.ScreenModeId}");
        Set($"desktopwidth:i:{geometry.DesktopWidth}");
        Set($"desktopheight:i:{geometry.DesktopHeight}");
        var w = geometry.Window;
        Set(string.Create(CultureInfo.InvariantCulture, $"winposstr:s:0,1,{w.Left},{w.Top},{w.Right},{w.Bottom}"));
        Set("smart sizing:i:0");
        Set("use multimon:i:0");
        if (settings.Zoom is { } zoom)
        {
            Set($"desktopscalefactor:i:{zoom}");
        }

        if (settings.Drives is { } drives)
        {
            Set($"drivestoredirect:s:{DriveList(drives)}");
        }

        if (Username(settings) is { } username)
        {
            Set($"username:s:{username}");
            Set("prompt for credentials:i:0");
            Set("promptcredentialonce:i:1");
        }

        var text = new StringBuilder();
        foreach (var (_, line) in lines)
        {
            text.Append(line).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The username for the file: <c>domain\user</c> when a domain is set and the user has none.</summary>
    public static string? Username(RdpSettings settings, string? credentialUser = null)
    {
        var user = settings.Username ?? credentialUser;
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(settings.Domain) || user.Contains('\\') || user.Contains('@')
            ? user
            : $"{settings.Domain}\\{user}";
    }

    /// <summary>
    /// Turns a <see cref="RdpSettings.Drives"/> spec into the <c>drivestoredirect</c> value: <c>*</c>
    /// stays <c>*</c>; letters and drive types become <c>C:\;D:\</c>, <c>dynamic</c> becomes
    /// <c>DynamicDrives</c>, and a <c>-</c> item takes a drive out again.
    /// </summary>
    public static string DriveList(string spec, IEnumerable<(string Letter, DriveType Type)>? localDrives = null)
    {
        var items = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0 || items.Contains("*"))
        {
            return "*";
        }

        localDrives ??= DriveInfo.GetDrives().Select(drive => (drive.Name[..2].ToUpperInvariant(), drive.DriveType));
        var known = localDrives.ToList();
        var chosen = new List<string>();
        var dynamic = false;

        foreach (var item in items)
        {
            var exclude = item.StartsWith('-');
            var name = item.TrimStart('-').ToLowerInvariant();
            IEnumerable<string> letters = name switch
            {
                "fixed" => known.Where(d => d.Type == DriveType.Fixed).Select(d => d.Letter),
                "removable" => known.Where(d => d.Type == DriveType.Removable).Select(d => d.Letter),
                "network" => known.Where(d => d.Type == DriveType.Network).Select(d => d.Letter),
                "cdrom" => known.Where(d => d.Type == DriveType.CDRom).Select(d => d.Letter),
                "dynamic" => [],
                _ when name.Length is 1 or 2 && char.IsLetter(name[0]) => [$"{char.ToUpperInvariant(name[0])}:"],
                _ => []
            };

            if (name == "dynamic")
            {
                dynamic = !exclude;
            }

            foreach (var letter in letters)
            {
                chosen.Remove(letter);
                if (!exclude)
                {
                    chosen.Add(letter);
                }
            }
        }

        var list = chosen.Select(letter => letter + "\\").ToList();
        if (dynamic)
        {
            list.Add("DynamicDrives");
        }

        return string.Join(";", list);
    }

    /// <summary>The key of a <c>key:type:value</c> line, or null for a blank or malformed one.</summary>
    private static string? KeyOf(string line)
    {
        var parts = line.Split(':', 3);
        return parts.Length == 3 && parts[0].Trim().Length > 0 && parts[1].Trim().Length == 1 ? parts[0].Trim() : null;
    }
}

/// <summary>The real monitors and window frame, from Win32.</summary>
public static class Displays
{
    private const int MONITORINFOF_PRIMARY = 1;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CYCAPTION = 4;
    private const int SM_CXPADDEDBORDER = 92;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfoNative
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoNative info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>The monitors in the order Windows enumerates them. Needs a per-monitor DPI aware process.</summary>
    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MonitorInfoNative { Size = Marshal.SizeOf<MonitorInfoNative>() };
            if (GetMonitorInfo(handle, ref info))
            {
                var dpi = GetDpiForMonitor(handle, 0, out var dpiX, out _) == 0 ? (int)dpiX : 96;
                monitors.Add(new MonitorInfo(ToPixels(info.Monitor), ToPixels(info.Work), (info.Flags & MONITORINFOF_PRIMARY) != 0, dpi));
            }

            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    /// <summary>The frame of a normal resizable window at <paramref name="dpi"/>.</summary>
    public static WindowChrome Chrome(int dpi)
    {
        var d = (uint)dpi;
        var padded = GetSystemMetricsForDpi(SM_CXPADDEDBORDER, d);
        var horizontal = GetSystemMetricsForDpi(SM_CXSIZEFRAME, d) + padded;
        var vertical = GetSystemMetricsForDpi(SM_CYSIZEFRAME, d) + padded;
        return new WindowChrome(horizontal, GetSystemMetricsForDpi(SM_CYCAPTION, d) + vertical, vertical);
    }

    private static PixelRect ToPixels(Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
