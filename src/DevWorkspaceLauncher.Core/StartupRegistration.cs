using Microsoft.Win32;

namespace DevWorkspaceLauncher;

/// <summary>"Start with Windows": the app's value under the current user's <c>Run</c> key.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Whether the Run key starts this executable.</summary>
    public static bool IsEnabled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppIdentity.CodeName) is string command
            && command.Contains(executable, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starts <paramref name="executable"/> with <paramref name="arguments"/> at logon, or stops doing so.</summary>
    public static void Set(bool enabled, string executable, string arguments)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(AppIdentity.CodeName, $"\"{executable}\" {arguments}");
        }
        else
        {
            key.DeleteValue(AppIdentity.CodeName, throwOnMissingValue: false);
        }
    }
}
