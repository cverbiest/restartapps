using Microsoft.Win32;

namespace DevWorkspaceLauncher;

/// <summary>Whether, and how, the app starts at logon.</summary>
public enum StartupMode
{
    /// <summary>Not started at logon.</summary>
    Off,

    /// <summary>Started at logon with only the tray icon showing (<c>/tray</c>).</summary>
    Tray,

    /// <summary>Started at logon with the main window open.</summary>
    Window
}

/// <summary>"Start with Windows": the app's value under the current user's <c>Run</c> key.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string TrayArgument = "/tray";

    /// <summary>How the Run key starts <paramref name="executable"/>; Off when it starts another copy.</summary>
    public static StartupMode Mode(string executable) => ModeOf(Command(), executable);

    /// <summary>The executable the Run key starts when that is not <paramref name="executable"/>; else null.</summary>
    public static string? OtherExecutable(string executable) => OtherExecutableOf(Command(), executable);

    /// <summary>Starts <paramref name="executable"/> at logon as <paramref name="mode"/> says, or stops doing so.</summary>
    public static void Set(StartupMode mode, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (mode == StartupMode.Off)
        {
            key.DeleteValue(AppIdentity.CodeName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(AppIdentity.CodeName, CommandFor(mode, executable));
        }
    }

    /// <summary>How <paramref name="command"/>, a Run value, starts <paramref name="executable"/>.</summary>
    public static StartupMode ModeOf(string? command, string executable)
    {
        var (started, arguments) = Split(command);
        if (!string.Equals(started, executable, StringComparison.OrdinalIgnoreCase))
        {
            return StartupMode.Off;
        }

        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(argument => string.Equals(argument, TrayArgument, StringComparison.OrdinalIgnoreCase))
            ? StartupMode.Tray
            : StartupMode.Window;
    }

    /// <summary>The executable <paramref name="command"/> starts when that is not <paramref name="executable"/>.</summary>
    public static string? OtherExecutableOf(string? command, string executable) =>
        Split(command).Executable is { } other && !string.Equals(other, executable, StringComparison.OrdinalIgnoreCase)
            ? other
            : null;

    /// <summary>The Run value that starts <paramref name="executable"/> as <paramref name="mode"/> says.</summary>
    public static string CommandFor(StartupMode mode, string executable) =>
        mode == StartupMode.Tray ? $"\"{executable}\" {TrayArgument}" : $"\"{executable}\"";

    /// <summary>
    /// A Run value's executable (the quoted path, else everything up to the first space) and the
    /// arguments after it.
    /// </summary>
    private static (string? Executable, string Arguments) Split(string? command)
    {
        command = command?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            return (null, string.Empty);
        }

        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? (command[1..end], command[(end + 1)..]) : (null, string.Empty);
        }

        var space = command.IndexOf(' ');
        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..]);
    }

    private static string? Command()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppIdentity.CodeName) as string;
    }
}
