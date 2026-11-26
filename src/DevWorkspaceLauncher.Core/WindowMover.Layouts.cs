using System.Runtime.InteropServices;
using System.Text;

namespace DevWorkspaceLauncher;

/// <summary>Records the positions of the open windows per set of monitors, and puts them back.</summary>
public static partial class WindowMover
{
    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_MINIMIZE = 6;
    private const int WPF_RESTORETOMAXIMIZED = 2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    /// <summary>The monitors connected now, as a layout key and in words.</summary>
    public static (string Key, string Description) CurrentMonitors()
    {
        var monitors = Displays.Monitors();
        return (MonitorSetup.KeyOf(monitors), MonitorSetup.Describe(monitors));
    }

    /// <summary>
    /// Records where every window the taskbar shows is, for the monitors connected now, replacing what
    /// was recorded for them before.
    /// </summary>
    public static void RecordLayout(Action<string> log)
    {
        var (key, description) = CurrentMonitors();
        var windows = new List<RecordedWindow>();
        foreach (var window in TaskbarWindows())
        {
            var placement = new WindowPlacementNative { Length = Marshal.SizeOf<WindowPlacementNative>() };
            if (!GetWindowPlacement(window, ref placement))
            {
                continue;
            }

            var normal = placement.NormalPosition;
            var state = placement.ShowCmd switch
            {
                SW_SHOWMAXIMIZED => RecordedWindowState.Maximized,
                SW_SHOWMINIMIZED or SW_MINIMIZE or SW_SHOWMINNOACTIVE => RecordedWindowState.Minimized,
                _ => RecordedWindowState.Normal
            };
            windows.Add(new RecordedWindow(
                ProgramOf(window), TitleOf(window),
                normal.Left, normal.Top, normal.Right, normal.Bottom,
                state, (placement.Flags & WPF_RESTORETOMAXIMIZED) != 0));
        }

        var layouts = WindowLayoutStore.Load();
        WindowLayoutStore.Put(layouts, new WindowLayout
        {
            Monitors = key,
            Description = description,
            Recorded = DateTime.Now,
            Windows = windows
        });
        WindowLayoutStore.Save(layouts);
        log($"Recorded the positions of {windows.Count} window(s) for {description}.");
    }

    /// <summary>
    /// Puts the open windows where they were recorded for the monitors connected now. Windows that
    /// were not recorded, and recorded windows that are not open, are left alone. A minimized window
    /// stays minimized, but comes back at its recorded place.
    /// </summary>
    public static void RestoreLayout(Action<string> log)
    {
        var (key, description) = CurrentMonitors();
        var layout = WindowLayoutStore.Load().FirstOrDefault(layout => layout.Monitors == key);
        if (layout is null)
        {
            log($"No window positions recorded for {description}. Record them first: Settings, or the tray menu.");
            return;
        }

        var open = TaskbarWindows();
        var pairs = WindowMatcher.Match(
            layout.Windows.Select(window => window.Identity).ToList(),
            open.Select(window => new WindowIdentity(ProgramOf(window), TitleOf(window))).ToList());

        var foreground = GetForegroundWindow();
        var moved = 0;
        var failed = new List<string>();
        foreach (var (recordedIndex, openIndex) in pairs)
        {
            var window = open[openIndex];
            if (IsHungAppWindow(window) || !Apply(window, layout.Windows[recordedIndex]))
            {
                failed.Add(TitleOf(window));
            }
            else
            {
                moved++;
            }
        }

        if (foreground != IntPtr.Zero)
        {
            SetForegroundWindow(foreground);
        }

        var notOpen = layout.Windows.Count - pairs.Count;
        log($"Restored the positions of {moved} window(s) for {description}"
            + (notOpen > 0 ? $"; {notOpen} recorded window(s) are not open." : "."));
        if (failed.Count > 0)
        {
            log($"Could not move {string.Join(", ", failed.Select(title => $"'{title}'"))} - not responding, or running as administrator.");
        }
    }

    /// <summary>Gives <paramref name="window"/> the recorded place and state.</summary>
    private static bool Apply(IntPtr window, RecordedWindow recorded)
    {
        var placement = new WindowPlacementNative { Length = Marshal.SizeOf<WindowPlacementNative>() };
        if (!GetWindowPlacement(window, ref placement))
        {
            return false;
        }

        var minimizedNow = placement.ShowCmd is SW_SHOWMINIMIZED or SW_MINIMIZE or SW_SHOWMINNOACTIVE;
        var maximize = recorded.State == RecordedWindowState.Maximized
            || recorded.State == RecordedWindowState.Minimized && recorded.RestoreToMaximized;
        placement.NormalPosition = new Rect { Left = recorded.Left, Top = recorded.Top, Right = recorded.Right, Bottom = recorded.Bottom };
        placement.Flags = minimizedNow && maximize ? WPF_RESTORETOMAXIMIZED : 0;
        // A maximized window given a new placement only takes the new restore position; it stays
        // maximized where it is. So show it normally at the new place first, then maximize it there.
        placement.ShowCmd = minimizedNow ? SW_SHOWMINNOACTIVE : SW_SHOWNOACTIVATE;

        if (!SetWindowPlacement(window, ref placement))
        {
            return false;
        }

        if (!minimizedNow)
        {
            // Moving to a monitor with another DPI makes the window rescale itself after the move;
            // a second placement puts it at the recorded size.
            SetWindowPlacement(window, ref placement);
            if (maximize)
            {
                ShowWindow(window, SW_SHOWMAXIMIZED);
            }
        }

        return true;
    }

    /// <summary>The file name of the program that owns <paramref name="window"/>, or "" when it cannot be read.</summary>
    private static string ProgramOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var name = new StringBuilder(1024);
            var size = name.Capacity;
            return QueryFullProcessImageName(process, 0, name, ref size) ? Path.GetFileName(name.ToString()) : string.Empty;
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
