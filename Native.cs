using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RestartApps;

internal static class CommandLineParser
{
    public static string GetProcessName(string commandline)
    {
        var executable = GetExecutablePath(commandline);
        return string.IsNullOrWhiteSpace(executable)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(executable);
    }

    private static string GetExecutablePath(string commandline)
    {
        commandline = commandline.Trim();
        if (commandline.Length == 0)
        {
            return string.Empty;
        }

        if (commandline[0] == '"')
        {
            var closingQuote = commandline.IndexOf('"', 1);
            return closingQuote > 1
                ? commandline[1..closingQuote]
                : string.Empty;
        }

        var firstSpace = commandline.IndexOf(' ');
        return firstSpace >= 0
            ? commandline[..firstSpace]
            : commandline;
    }
}

/// <summary>
/// Finds and closes top-level windows by AppUserModelID.
/// <para>
/// A browser-hosted PWA has no process of its own: Chromium runs one browser process per
/// user-data-dir, and every profile and installed app is just another window inside it. Killing by
/// executable name would therefore take down the whole browser. The shell does give each PWA window
/// a distinct AppUserModelID, so closing exactly those windows leaves the rest of the browser alone.
/// </para>
/// </summary>
internal static class WindowFinder
{
    private const uint WM_CLOSE = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hWnd,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(IntPtr pvar);

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint count);
        int GetAt(uint index, out PropertyKey key);
        int GetValue(ref PropertyKey key, IntPtr value);
        int SetValue(ref PropertyKey key, IntPtr value);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    // PKEY_AppUserModel_ID
    private static PropertyKey AppUserModelIdKey => new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5
    };

    public sealed record WindowInfo(IntPtr Handle, string Title, string Aumid);

    /// <summary>Every visible top-level window whose AppUserModelID matches the wildcard pattern.</summary>
    public static List<WindowInfo> FindByAumid(string pattern)
    {
        var matcher = BuildMatcher(pattern);
        var matches = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var aumid = GetAumid(hWnd);
            if (aumid is null || !matcher.IsMatch(aumid))
            {
                return true;
            }

            matches.Add(new WindowInfo(hWnd, GetTitle(hWnd), aumid));
            return true;
        }, IntPtr.Zero);

        return matches;
    }

    /// <summary>Asks the window to close. Posted, not sent, so an unresponsive app cannot block us.</summary>
    public static void Close(IntPtr hWnd) => PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public static bool StillOpen(IntPtr hWnd) => IsWindow(hWnd) && IsWindowVisible(hWnd);

    private static Regex BuildMatcher(string pattern)
    {
        var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string? GetAumid(IntPtr hWnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        IPropertyStore? store = null;
        var variant = Marshal.AllocCoTaskMem(32);

        try
        {
            // Zero the PROPVARIANT so a failed GetValue cannot be mistaken for a string.
            for (var offset = 0; offset < 32; offset += 8)
            {
                Marshal.WriteInt64(variant, offset, 0);
            }

            if (SHGetPropertyStoreForWindow(hWnd, ref iid, out store) != 0 || store is null)
            {
                return null;
            }

            var key = AppUserModelIdKey;
            if (store.GetValue(ref key, variant) != 0)
            {
                return null;
            }

            // VT_LPWSTR == 31; the pointer sits at the first field after the 8-byte header.
            var type = (ushort)Marshal.ReadInt16(variant);
            if (type != 31)
            {
                return null;
            }

            var text = Marshal.ReadIntPtr(variant, 8);
            return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text);
        }
        catch
        {
            return null;
        }
        finally
        {
            PropVariantClear(variant);
            Marshal.FreeCoTaskMem(variant);
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }
}

/// <summary>Reads the target and arguments out of a .lnk file through the Windows Script Host COM object.</summary>
internal static class ShortcutResolver
{
    public static (string Target, string Arguments) Resolve(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return (string.Empty, string.Empty);
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return (string.Empty, string.Empty);
            }

            dynamic link = shell.CreateShortcut(shortcutPath);
            string target = link.TargetPath ?? string.Empty;
            string arguments = link.Arguments ?? string.Empty;
            return (target, arguments);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }
}
