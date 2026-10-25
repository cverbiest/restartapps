using System.Diagnostics;
using System.Text.Json;

namespace DevWorkspaceLauncher;

/// <summary>Whether an entry is a plain folder or a .code-workspace file.</summary>
public enum WorkspaceKind
{
    Folder,
    WorkspaceFile
}

/// <summary>One workspace VS Code has opened at least once.</summary>
/// <param name="Id">The workspaceStorage directory name, used to probe whether it is open.</param>
/// <param name="Uri">The URI exactly as VS Code stored it; this is what gets handed back to VS Code.</param>
/// <param name="Location">A local path, or a remote label such as "SSH: host".</param>
/// <param name="LastUsed">The storage directory's last write time, which is when it was last opened.</param>
public sealed record VsCodeWorkspace(
    string Id,
    string Uri,
    WorkspaceKind Kind,
    string DisplayName,
    string Location,
    DateTime LastUsed);

/// <summary>
/// Decodes the URIs VS Code stores. Parsed by hand rather than through <see cref="Uri"/> because the
/// remote authority carries percent-escaped '+' separators that the framework parser does not
/// unescape consistently.
/// </summary>
public static class VsCodeUri
{
    private const string WorkspaceExtension = ".code-workspace";

    public static (WorkspaceKind Kind, string DisplayName, string Location) Describe(string uri)
    {
        var (scheme, authority, path) = Split(uri);

        var kind = path.EndsWith(WorkspaceExtension, StringComparison.OrdinalIgnoreCase)
            ? WorkspaceKind.WorkspaceFile
            : WorkspaceKind.Folder;

        var name = LastSegment(path);
        if (kind == WorkspaceKind.WorkspaceFile)
        {
            name = name[..^WorkspaceExtension.Length];
        }

        string location;
        if (scheme.Length == 0)
        {
            // Not a URI at all: showing it verbatim beats showing nothing.
            location = path;
        }
        else if (string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            location = ToWindowsPath(path);
        }
        else
        {
            location = RemoteLabel(authority);
        }

        return (kind, name, location);
    }

    private static (string Scheme, string Authority, string Path) Split(string uri)
    {
        var mark = uri.IndexOf("://", StringComparison.Ordinal);
        if (mark < 0)
        {
            return (string.Empty, string.Empty, Unescape(uri));
        }

        var scheme = uri[..mark];
        var rest = uri[(mark + 3)..];
        var slash = rest.IndexOf('/');

        return slash < 0
            ? (scheme, Unescape(rest), string.Empty)
            : (scheme, Unescape(rest[..slash]), Unescape(rest[slash..]));
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            // A malformed escape is not worth failing over; the raw text still identifies the entry.
            return value;
        }
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string ToWindowsPath(string path)
    {
        return path.TrimStart('/').TrimEnd('/').Replace('/', '\\');
    }

    private static string RemoteLabel(string authority)
    {
        var plus = authority.IndexOf('+');
        if (plus < 0)
        {
            return authority;
        }

        var value = authority[(plus + 1)..];
        return authority[..plus].ToLowerInvariant() switch
        {
            "ssh-remote" => $"SSH: {value}",
            "wsl" => $"WSL: {value}",
            // The dev container authority is a hex-encoded JSON blob; showing it would be noise.
            "dev-container" => "Dev Container",
            "attached-container" => "Container",
            _ => authority
        };
    }
}

/// <summary>
/// Reads the workspaces VS Code has opened. VS Code keeps one directory per workspace under
/// workspaceStorage, holding a workspace.json that names it; the directory's last write time is when
/// it was last opened. That is used instead of the recent-paths list in state.vscdb, which is a
/// SQLite database and is not present on every installation.
/// </summary>
public static class VsCodeWorkspaces
{
    public static string DefaultStorageRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Code",
        "User",
        "workspaceStorage");

    /// <summary>Every workspace VS Code knows about, most recently used first.</summary>
    public static IReadOnlyList<VsCodeWorkspace> Discover(string storageRoot)
    {
        if (!Directory.Exists(storageRoot))
        {
            return [];
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(storageRoot);
        }
        catch
        {
            return [];
        }

        var found = new Dictionary<string, VsCodeWorkspace>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            var workspace = ReadEntry(directory);
            if (workspace is null)
            {
                continue;
            }

            // The same folder can have several storage directories; the newest one is the live one.
            if (found.TryGetValue(workspace.Uri, out var existing) && existing.LastUsed >= workspace.LastUsed)
            {
                continue;
            }

            found[workspace.Uri] = workspace;
        }

        return found.Values
            .OrderByDescending(workspace => workspace.LastUsed)
            .ThenBy(workspace => workspace.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether VS Code is running at all, so a whole refresh can skip the disk when it is not.</summary>
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("Code");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>
    /// Whether a VS Code window currently has this workspace open. A live window keeps its workspace
    /// state database open, so an exclusive open of that file fails exactly while the window exists.
    /// Window titles cannot answer this: window.title is user-configurable and need not contain the
    /// path at all.
    /// </summary>
    public static bool IsOpen(string storageRoot, string id)
    {
        var file = Path.Combine(storageRoot, id, "state.vscdb");
        if (!File.Exists(file))
        {
            return false;
        }

        try
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            // Read-only or otherwise unopenable: report closed rather than claiming a window exists.
            return false;
        }
    }

    private static VsCodeWorkspace? ReadEntry(string directory)
    {
        var file = Path.Combine(directory, "workspace.json");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var uri = ReadUri(document.RootElement);
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            var (kind, name, location) = VsCodeUri.Describe(uri);
            return new VsCodeWorkspace(
                Path.GetFileName(directory),
                uri,
                kind,
                name,
                location,
                Directory.GetLastWriteTimeUtc(directory));
        }
        catch
        {
            // An unreadable or half-written entry is skipped rather than failing the whole scan.
            return null;
        }
    }

    private static string? ReadUri(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.String)
        {
            return folder.GetString();
        }

        return root.TryGetProperty("workspace", out var workspace) && workspace.ValueKind == JsonValueKind.String
            ? workspace.GetString()
            : null;
    }
}

/// <summary>
/// Builds the command that opens a workspace. VS Code answers a request for a workspace it already
/// has open by focusing that window instead of opening a second one, so the same command serves both
/// the Launch and the Focus button.
/// </summary>
public static class VsCodeLauncher
{
    public static string BuildArguments(string uri, WorkspaceKind kind)
    {
        return kind == WorkspaceKind.WorkspaceFile
            ? $"--file-uri \"{uri}\""
            : $"--folder-uri \"{uri}\"";
    }

    /// <summary>Where VS Code might be, best first.</summary>
    public static IEnumerable<string> CandidatePaths(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Environment.ExpandEnvironmentVariables(configured);
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "Code.exe");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft VS Code",
            "Code.exe");

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, "code.cmd");
        }
    }

    /// <param name="exists">Injected so the resolution order can be tested without a VS Code install.</param>
    public static string? ResolveExecutable(string? configured, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        foreach (var candidate in CandidatePaths(configured))
        {
            try
            {
                if (exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // An unusable PATH entry just means this candidate is not the one.
            }
        }

        return null;
    }

    public static ProcessStartInfo CreateStartInfo(string executable, string arguments)
    {
        // A .cmd shim is not an executable image, so it has to go through the command processor.
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{executable}\" {arguments}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}

/// <summary>One line of the Workspaces grid.</summary>
public sealed class WorkspaceRow
{
    public required string Uri { get; init; }

    /// <summary>The storage id, or null for a favourite VS Code has no storage directory for.</summary>
    public string? Id { get; init; }

    public required WorkspaceKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public bool Preferred { get; init; }

    /// <summary>The favourite's window title pattern, when it has one. See <see cref="WorkspaceFavorite.WindowTitle"/>.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Filled in per refresh by probing the workspace's state database.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Session-only ticks, like the Kill and Restart ticks on the Apps tab.</summary>
    public bool Close { get; set; }

    public bool Open { get; set; }
}

/// <summary>Why a close attempt did or did not act.</summary>
public enum WindowMatch
{
    /// <summary>Exactly one window matched, which is the only case in which anything is closed.</summary>
    Single,

    /// <summary>No window matched, so there is nothing to close.</summary>
    None,

    /// <summary>Several windows matched. Closing would be a guess, so nothing is done.</summary>
    Ambiguous
}

/// <summary>
/// Finds the window belonging to a workspace. VS Code puts every window in one process under one
/// AppUserModelID, so the title is the only thing that tells them apart - and the title is whatever
/// window.title makes it. Matching therefore refuses to guess: it acts only on a single match.
/// </summary>
public static class VsCodeWindows
{
    /// <summary>
    /// Picks the window for a workspace out of the open VS Code titles.
    /// </summary>
    /// <param name="titles">The titles of every open VS Code window.</param>
    /// <param name="displayName">The workspace name, used when there is no explicit pattern.</param>
    /// <param name="pattern">An explicit wildcard title pattern, which wins when present.</param>
    /// <returns>The outcome and, for a single match, the index into <paramref name="titles"/>.</returns>
    public static (WindowMatch Match, int Index) Select(
        IReadOnlyList<string> titles,
        string displayName,
        string? pattern)
    {
        var matches = MatchAll(titles, displayName, pattern);

        return matches.Count switch
        {
            1 => (WindowMatch.Single, matches[0]),
            0 => (WindowMatch.None, -1),
            _ => (WindowMatch.Ambiguous, -1)
        };
    }

    /// <summary>The indexes of every title that belongs to this workspace.</summary>
    public static List<int> MatchAll(IReadOnlyList<string> titles, string displayName, string? pattern)
    {
        var matches = new List<int>();

        for (var index = 0; index < titles.Count; index++)
        {
            if (IsMatch(titles[index], displayName, pattern))
            {
                matches.Add(index);
            }
        }

        return matches;
    }

    private static bool IsMatch(string title, string displayName, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            return WindowFinder.MatchesPattern(pattern, title);
        }

        // The default title starts with the workspace name; a customised one usually still contains it.
        return !string.IsNullOrWhiteSpace(displayName)
            && title.Contains(displayName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Decides which workspaces the grid shows and in which order.</summary>
public static class WorkspaceRows
{
    public static List<WorkspaceRow> Build(
        IReadOnlyList<VsCodeWorkspace> discovered,
        IReadOnlyList<WorkspaceFavorite> favorites,
        bool showAll)
    {
        var workspaceByUri = new Dictionary<string, VsCodeWorkspace>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in discovered)
        {
            workspaceByUri[workspace.Uri] = workspace;
        }

        var favoriteByUri = new Dictionary<string, WorkspaceFavorite>(StringComparer.OrdinalIgnoreCase);
        foreach (var favorite in favorites)
        {
            favoriteByUri[favorite.Uri] = favorite;
        }

        var rows = new List<WorkspaceRow>();

        foreach (var favorite in favorites)
        {
            var known = workspaceByUri.GetValueOrDefault(favorite.Uri);

            // In the all-recent view a known favourite is listed below, in recency order; only the
            // ones VS Code has forgotten need listing here, so that nothing silently disappears.
            if (showAll && known is not null)
            {
                continue;
            }

            rows.Add(FromFavorite(favorite, known));
        }

        if (!showAll)
        {
            return rows;
        }

        foreach (var workspace in discovered)
        {
            var favorite = favoriteByUri.GetValueOrDefault(workspace.Uri);
            rows.Add(new WorkspaceRow
            {
                Uri = workspace.Uri,
                Id = workspace.Id,
                Kind = workspace.Kind,
                Name = Named(favorite, workspace.DisplayName),
                Location = workspace.Location,
                Preferred = favorite is not null,
                WindowTitle = favorite?.WindowTitle
            });
        }

        return rows;
    }

    private static WorkspaceRow FromFavorite(WorkspaceFavorite favorite, VsCodeWorkspace? known)
    {
        if (known is not null)
        {
            return new WorkspaceRow
            {
                Uri = known.Uri,
                Id = known.Id,
                Kind = known.Kind,
                Name = Named(favorite, known.DisplayName),
                Location = known.Location,
                Preferred = true,
                WindowTitle = favorite.WindowTitle
            };
        }

        // VS Code has no storage directory for this one, so everything comes from the URI itself and
        // it can be launched but not probed.
        var (kind, name, location) = VsCodeUri.Describe(favorite.Uri);
        return new WorkspaceRow
        {
            Uri = favorite.Uri,
            Id = null,
            Kind = kind,
            Name = Named(favorite, name),
            Location = location,
            Preferred = true,
            WindowTitle = favorite.WindowTitle
        };
    }

    private static string Named(WorkspaceFavorite? favorite, string fallback)
    {
        return string.IsNullOrWhiteSpace(favorite?.Name) ? fallback : favorite.Name;
    }
}
