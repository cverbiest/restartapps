using System.Runtime.InteropServices;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>One line of the tray menu: a command or a separator (<see cref="Separator"/>).</summary>
internal sealed record TrayEntry(string Text, Action? Run = null)
{
    public bool Checked { get; init; }

    /// <summary>Shown in bold; the command the menu leads with.</summary>
    public bool Default { get; init; }

    public bool Enabled { get; init; } = true;

    public static TrayEntry Separator { get; } = new("-");

    public bool IsSeparator => ReferenceEquals(this, Separator);
}

/// <summary>
/// A notification-area icon with a native popup menu. WinUI has no tray control, so this is plain
/// Win32: a hidden window receives the icon's mouse messages on the UI thread, and the menu is rebuilt
/// from <c>buildMenu</c> each time it opens, so it always shows the current favorites.
/// Commands run after the menu has closed, on the UI thread's dispatcher.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_NULL = 0x0000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_TRAY = 0x8000 + 1; // WM_APP + 1
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_WARNING = 0x2;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public int Size;
        public uint Style;
        public WindowProc Proc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[] small, uint count);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private const string ClassName = "DevWorkspaceLauncher.TrayIcon";

    // Kept in a field: the window class holds only a native pointer to it.
    private readonly WindowProc _proc;
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly uint _taskbarCreated;
    private readonly string _tooltip;
    private readonly Func<IReadOnlyList<TrayEntry>> _buildMenu;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    public TrayIcon(string tooltip, Func<IReadOnlyList<TrayEntry>> buildMenu)
    {
        _tooltip = tooltip;
        _buildMenu = buildMenu;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _proc = OnMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = Marshal.SizeOf<WindowClass>(),
            Proc = _proc,
            Instance = instance,
            ClassName = ClassName
        };
        RegisterClassEx(ref windowClass);

        // A hidden top-level window rather than a message-only one: the menu needs a window that can
        // take the foreground, or it does not close when clicking elsewhere.
        _window = CreateWindowEx(0, ClassName, tooltip, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        // The app's own icon, from its exe (ApplicationIcon in the project).
        var small = new IntPtr[1];
        _ownsIcon = Environment.ProcessPath is { } exe && ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero;
        _icon = _ownsIcon ? small[0] : LoadIcon(IntPtr.Zero, 32512 /* IDI_APPLICATION */);

        Add();
    }

    /// <summary>Shows a balloon notification from the icon, for problems while the window is hidden.</summary>
    public void ShowWarning(string title, string text)
    {
        var data = Data(NIF_INFO);
        data.InfoTitle = Truncate(title, 63);
        data.Info = Truncate(text, 255);
        data.InfoFlags = NIIF_WARNING;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        DestroyWindow(_window);
        if (_ownsIcon)
        {
            DestroyIcon(_icon);
        }
    }

    private void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NotifyIconData Data(uint flags) => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = flags,
        CallbackMessage = WM_TRAY,
        Icon = _icon,
        Tip = Truncate(_tooltip, 127),
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_TRAY && (uint)lParam is WM_LBUTTONUP or WM_RBUTTONUP)
        {
            ShowMenu();
            return IntPtr.Zero;
        }

        if (message == _taskbarCreated)
        {
            // Explorer restarted and lost every tray icon; put ours back.
            Add();
            return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var commands = new List<Action>();
        var menu = Build(_buildMenu(), commands);
        try
        {
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, point.X, point.Y, _window, IntPtr.Zero);
            PostMessage(_window, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (chosen > 0)
            {
                _dispatcher.TryEnqueue(() => commands[chosen - 1]());
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>Builds a native menu; command ids are 1-based indexes into <paramref name="commands"/>.</summary>
    private static IntPtr Build(IReadOnlyList<TrayEntry> entries, List<Action> commands)
    {
        var menu = CreatePopupMenu();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsSeparator)
            {
                AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                continue;
            }

            var state = (entry.Checked ? MF_CHECKED : 0) | (entry.Enabled ? 0 : MF_GRAYED);
            if (entry.Run is not null)
            {
                commands.Add(entry.Run);
            }

            AppendMenu(menu, MF_STRING | state, entry.Run is null ? IntPtr.Zero : commands.Count, entry.Text.Replace("&", "&&"));

            if (entry.Default)
            {
                SetMenuDefaultItem(menu, (uint)i, 1);
            }
        }

        return menu;
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
