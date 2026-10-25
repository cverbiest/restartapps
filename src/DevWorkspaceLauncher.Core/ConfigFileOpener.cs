using System.Diagnostics;

namespace DevWorkspaceLauncher;

public static class ConfigFileOpener
{
    /// <summary>Opens the file in the editor associated with .json, falling back to Notepad.</summary>
    public static void Open(string path, Action<string> log)
    {
        if (!File.Exists(path))
        {
            log($"Configuration file not found: {path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            log($"Opened {path} in the default editor.");
        }
        catch (Exception ex)
        {
            // No association for .json - fall back to Notepad.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
                log($"Opened {path} in Notepad.");
            }
            catch (Exception fallbackEx)
            {
                log($"Failed to open configuration: {ex.Message} / {fallbackEx.Message}");
            }
        }
    }
}
