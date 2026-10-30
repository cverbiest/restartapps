using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>
/// What the launcher keeps about an SSH host, keyed by its alias. The host itself lives in
/// <c>~/.ssh/config</c>; only hosts with a setting are written.
/// </summary>
public sealed class SshHostSettings : IFavorite
{
    public required string Alias { get; set; }

    /// <summary>Shown on the start page and in the tray menu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Favorite { get; set; }

    /// <summary>Its place among the favorites; see <see cref="IFavorite.FavoriteOrder"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int FavoriteOrder { get; set; }
}

/// <summary>
/// An SSH host as the lists and the start page show it: the entry from <c>~/.ssh/config</c> plus its
/// launcher settings. Pinning it creates the settings on first use. Two are equal when their aliases are,
/// so a list keeps its selection when the file is read again.
/// </summary>
public sealed class SshHost(SshHostEntry entry, ConfigurationFile config) : IFavorite, IEquatable<SshHost>
{
    public SshHostEntry Entry { get; } = entry;

    public string Alias => Entry.Alias;

    public string HostName => Entry.HostName ?? string.Empty;

    public string User => Entry.User ?? string.Empty;

    public string IdentityFile => Entry.IdentityFile ?? string.Empty;

    /// <summary>The other patterns of the Host line and the other identity files, which the tab does not edit.</summary>
    public string Also => string.Join(", ", Entry.OtherPatterns.Concat(Entry.MoreIdentityFiles));

    public bool Favorite
    {
        get => Settings?.Favorite ?? false;
        set => GetOrAddSettings().Favorite = value;
    }

    public int FavoriteOrder
    {
        get => Settings?.FavoriteOrder ?? 0;
        set => GetOrAddSettings().FavoriteOrder = value;
    }

    private SshHostSettings? Settings =>
        config.SshHosts.FirstOrDefault(settings => string.Equals(settings.Alias, Alias, StringComparison.OrdinalIgnoreCase));

    private SshHostSettings GetOrAddSettings()
    {
        if (Settings is { } settings)
        {
            return settings;
        }

        settings = new SshHostSettings { Alias = Alias };
        config.SshHosts.Add(settings);
        return settings;
    }

    public bool Equals(SshHost? other) => other is not null && string.Equals(Alias, other.Alias, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as SshHost);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Alias);

    /// <summary>Drops settings that no longer say anything, so only pinned hosts are written.</summary>
    public static void Prune(ConfigurationFile config) =>
        config.SshHosts.RemoveAll(settings => !settings.Favorite && settings.FavoriteOrder == 0);
}

public sealed record SshConfigSnapshot(SshConfigDocument Document, string Text);

/// <summary>Raised when <c>~/.ssh/config</c> changed on disk since it was read, so a save would overwrite that.</summary>
public sealed class SshConfigChangedException(string path)
    : IOException($"{path} changed outside the launcher since it was read; it was not saved.");

/// <summary>Reads and writes <c>~/.ssh/config</c>.</summary>
public static class SshConfigStore
{
    private static readonly object Gate = new();
    private static bool _backedUp;

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    /// <summary>The file, or an empty one when it does not exist.</summary>
    public static SshConfigSnapshot Load() => Load(FilePath);

    public static SshConfigSnapshot Load(string path)
    {
        var text = ReadText(path);
        return new SshConfigSnapshot(SshConfigDocument.Parse(text), text);
    }

    /// <summary>
    /// Writes <paramref name="newText"/>, unless the file no longer holds <paramref name="expectedText"/>.
    /// The first save of a run keeps a copy of the file next to it. The new text goes to a temporary
    /// file that then replaces the original, so the file keeps its permissions (ssh checks them).
    /// </summary>
    public static void Save(string path, string expectedText, string newText)
    {
        lock (Gate)
        {
            if (ReadText(path) != expectedText)
            {
                throw new SshConfigChangedException(path);
            }

            var exists = File.Exists(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (exists && !_backedUp)
            {
                File.Copy(path, $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: false);
                _backedUp = true;
            }

            var encoding = exists && HasBom(path) ? new UTF8Encoding(true) : new UTF8Encoding(false);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, newText, encoding);
            if (exists)
            {
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }

    private static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static bool HasBom(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> start = stackalloc byte[3];
        return stream.Read(start) == 3 && start[0] == 0xEF && start[1] == 0xBB && start[2] == 0xBF;
    }
}

/// <summary>The terminal an SSH host is opened in.</summary>
public enum SshClient
{
    /// <summary>A WezTerm tab in the host's <c>SSH:&lt;alias&gt;</c> domain, which WezTerm makes from ~/.ssh/config.</summary>
    WezTerm,

    /// <summary><c>ssh &lt;alias&gt;</c> in a Windows Terminal tab, or a console window without Windows Terminal.</summary>
    WindowsTerminal
}

public sealed class SshClientConverter() : CaseInsensitiveEnumConverter<SshClient>("SshClient");

/// <summary>Opens a terminal on an SSH host, by its alias in ~/.ssh/config.</summary>
public static class SshLauncher
{
    /// <summary>The client to use: the configured one, else WezTerm when it is installed.</summary>
    public static SshClient Resolve(SshClient? configured) =>
        configured ?? (WezTermDirectory() is not null ? SshClient.WezTerm : SshClient.WindowsTerminal);

    public static async Task ConnectAsync(string alias, SshClient? configured, Action<string> log)
    {
        if (!SshConfigDocument.IsValidAlias(alias))
        {
            log($"Could not connect: '{alias}' is not an SSH host alias.");
            return;
        }

        try
        {
            if (Resolve(configured) == SshClient.WezTerm)
            {
                await ConnectWezTermAsync(alias, log);
            }
            else
            {
                ConnectWindowsTerminal(alias, log);
            }
        }
        catch (Exception ex)
        {
            log($"Could not connect to {alias}: {ex.Message}");
        }
    }

    private static void ConnectWindowsTerminal(string alias, Action<string> log)
    {
        var terminal = FindOnPath("wt.exe");
        Process.Start(terminal is not null
            ? new ProcessStartInfo(terminal, $"-w 0 nt --title {alias} ssh {alias}") { UseShellExecute = false }
            : new ProcessStartInfo("ssh.exe", alias) { UseShellExecute = false });
        log($"Connecting to {alias}...");
    }

    /// <summary>
    /// A new tab in the running WezTerm, else a new WezTerm window. The tab is spawned through the
    /// running GUI's own socket: <c>wezterm start --new-tab</c> and the CLI's socket discovery both end up
    /// starting a second WezTerm when an earlier one left a dead socket behind.
    /// </summary>
    private static async Task ConnectWezTermAsync(string alias, Action<string> log)
    {
        if (WezTermDirectory() is not { } directory)
        {
            log("Could not find WezTerm. Install it, or choose Windows Terminal for SSH hosts in Settings.");
            return;
        }

        var domain = $"SSH:{alias}";
        if (RunningWezTerm() is { } gui)
        {
            var spawn = new ProcessStartInfo(Path.Combine(directory, "wezterm.exe"))
            {
                ArgumentList = { "cli", "spawn", "--domain-name", domain },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            spawn.Environment["WEZTERM_UNIX_SOCKET"] = gui.Socket;

            using var process = Process.Start(spawn)!;
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (process.ExitCode == 0)
            {
                WindowFinder.BringToFront(gui.Window);
                log($"Connecting to {alias} in WezTerm...");
                return;
            }

            log($"WezTerm could not open a tab for {alias} ({LastLine(await error)}); opening a new window.");
        }

        Process.Start(new ProcessStartInfo(Path.Combine(directory, "wezterm-gui.exe"))
        {
            ArgumentList = { "connect", domain },
            UseShellExecute = false
        });
        log($"Connecting to {alias} in a new WezTerm window...");
    }

    /// <summary>The newest WezTerm window whose socket is there, with that socket.</summary>
    private static (string Socket, IntPtr Window)? RunningWezTerm()
    {
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "wezterm");
        foreach (var process in Process.GetProcessesByName("wezterm-gui").OrderByDescending(StartTime))
        {
            using (process)
            {
                var socket = Path.Combine(runtime, $"gui-sock-{process.Id}");
                if (process.MainWindowHandle != IntPtr.Zero && File.Exists(socket))
                {
                    return (socket, process.MainWindowHandle);
                }
            }
        }

        return null;

        static DateTime StartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }

    private static string? WezTermDirectory()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WezTerm");
        return File.Exists(Path.Combine(installed, "wezterm-gui.exe"))
            ? installed
            : FindOnPath("wezterm-gui.exe") is { } found ? Path.GetDirectoryName(found) : null;
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no message";

    private static string? FindOnPath(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory =>
            {
                try
                {
                    return Path.Combine(directory, file);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            })
            .FirstOrDefault(candidate => candidate is not null && File.Exists(candidate));
}
