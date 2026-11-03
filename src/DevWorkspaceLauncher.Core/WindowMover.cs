using System.Runtime.InteropServices;
using System.Text;

namespace DevWorkspaceLauncher;

/// <summary>Carries out a built-in action.</summary>
public static class BuiltInActionRunner
{
    public static void Run(BuiltInAction action, Action<string> log)
    {
        try
        {
            switch (action.BuiltIn)
            {
                case BuiltInActionId.MoveWindowsToMainDisplay:
                    WindowMover.MoveAllToPrimary(log);
                    break;
            }
        }
        catch (Exception ex)
        {
            log($"{action.Name} failed: {ex.Message}");
        }
    }
}

/// <summary>Moves windows that are on another monitor to the primary one.</summary>
public static class WindowMover
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_CHILD = 0x40000000;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int SW_SHOWMINNOACTIVE = 7;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_SHOWNOACTIVATE = 4;

    /// <summary>The windows of the shell itself - the taskbars and the desktop - which stay where they are.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementNative
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfoNative
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacementNative placement);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacementNative placement);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoNative info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    /// <summary>
    /// Moves every window the taskbar would show that is on another monitor to the primary one, keeping
    /// its size (shrunk to fit if needed), its place relative to the working area, and whether it is
    /// maximized or minimized. Windows of programs running as administrator cannot be moved from here.
    /// </summary>
    public static void MoveAllToPrimary(Action<string> log)
    {
        var primary = Displays.Monitors().FirstOrDefault(monitor => monitor.Primary);
        if (primary is null)
        {
            log("No primary monitor found; no windows moved.");
            return;
        }

        var moved = 0;
        var failed = new List<string>();
        foreach (var window in TaskbarWindows())
        {
            var source = MonitorOf(window);
            if (source is null || source.Value.Primary)
            {
                continue;
            }

            var placement = new WindowPlacementNative { Length = Marshal.SizeOf<WindowPlacementNative>() };
            if (!GetWindowPlacement(window, ref placement))
            {
                continue;
            }

            // The normal position is in workspace coordinates: relative to the primary monitor's
            // working area rather than the screen, which differ when the taskbar is at the top or left.
            var offsetX = primary.WorkingArea.Left - primary.Bounds.Left;
            var offsetY = primary.WorkingArea.Top - primary.Bounds.Top;
            var normal = new PixelRect(
                placement.NormalPosition.Left + offsetX,
                placement.NormalPosition.Top + offsetY,
                placement.NormalPosition.Right + offsetX,
                placement.NormalPosition.Bottom + offsetY);
            var target = Map(normal, source.Value.WorkingArea, primary.WorkingArea);

            placement.NormalPosition = new Rect
            {
                Left = target.Left - offsetX,
                Top = target.Top - offsetY,
                Right = target.Right - offsetX,
                Bottom = target.Bottom - offsetY
            };
            // A maximized window given a new placement only takes the new restore position; it stays
            // maximized where it is until it is restored. So restore it there, then maximize it again.
            var maximized = placement.ShowCmd == SW_SHOWMAXIMIZED;
            placement.ShowCmd = placement.ShowCmd is 2 or 6 or SW_SHOWMINNOACTIVE ? SW_SHOWMINNOACTIVE : SW_SHOWNOACTIVATE;

            if (SetWindowPlacement(window, ref placement))
            {
                if (maximized)
                {
                    ShowWindow(window, SW_SHOWMAXIMIZED);
                }

                moved++;
            }
            else
            {
                failed.Add(TitleOf(window));
            }
        }

        log(moved == 0 && failed.Count == 0
            ? "All windows are already on the main display."
            : $"Moved {moved} window(s) to the main display.");
        if (failed.Count > 0)
        {
            log($"Could not move {string.Join(", ", failed.Select(title => $"'{title}'"))} - probably running as administrator.");
        }
    }

    /// <summary>
    /// Where a window at <paramref name="window"/> on a working area <paramref name="source"/> goes on
    /// <paramref name="target"/>: the same size, shrunk to fit, at the same offset from the top left,
    /// pulled in so that it lies wholly inside.
    /// </summary>
    public static PixelRect Map(PixelRect window, PixelRect source, PixelRect target)
    {
        var width = Math.Min(window.Width, target.Width);
        var height = Math.Min(window.Height, target.Height);
        var left = Math.Clamp(target.Left + (window.Left - source.Left), target.Left, target.Right - width);
        var top = Math.Clamp(target.Top + (window.Top - source.Top), target.Top, target.Bottom - height);
        return new PixelRect(left, top, left + width, top + height);
    }

    /// <summary>The windows Alt+Tab and the taskbar show: visible, not cloaked, not tool windows, unowned or marked as app windows.</summary>
    private static List<IntPtr> TaskbarWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsCloaked(window))
            {
                return true;
            }

            var style = (long)GetWindowLongPtr(window, GWL_STYLE);
            var exStyle = (long)GetWindowLongPtr(window, GWL_EXSTYLE);
            var appWindow = (exStyle & WS_EX_APPWINDOW) != 0;
            var owned = GetWindow(window, GW_OWNER) != IntPtr.Zero;
            if ((style & WS_CHILD) != 0
                || (exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0 && !appWindow
                || owned && !appWindow
                || TitleOf(window).Length == 0)
            {
                return true;
            }

            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (!ShellClasses.Contains(name.ToString()))
            {
                found.Add(window);
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static (PixelRect WorkingArea, bool Primary)? MonitorOf(IntPtr window)
    {
        var info = new MonitorInfoNative { Size = Marshal.SizeOf<MonitorInfoNative>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref info))
        {
            return null;
        }

        return (new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom), (info.Flags & 1) != 0);
    }

    private static string TitleOf(IntPtr window)
    {
        var title = new StringBuilder(256);
        GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }
}
