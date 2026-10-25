using System.Diagnostics;

namespace DevWorkspaceLauncher;

/// <summary>Lists, opens, focuses and closes VS Code workspaces.</summary>
public static class WorkspaceController
{
    /// <summary>
    /// The rows to show: the preferred workspaces (or every recent one with <paramref name="showAll"/>),
    /// with their open state probed and the session-only Close/Open ticks restored.
    /// </summary>
    public static List<WorkspaceRow> LoadRows(
        ConfigurationFile config,
        bool showAll,
        IReadOnlyDictionary<string, (bool Close, bool Open)> ticks)
    {
        var discovered = VsCodeWorkspaces.Discover(VsCodeWorkspaces.DefaultStorageRoot);
        var rows = WorkspaceRows.Build(discovered, config.Workspaces, showAll);

        // With VS Code shut down nothing can be open, and the probe can be skipped entirely.
        var running = VsCodeWorkspaces.IsRunning();
        foreach (var row in rows)
        {
            row.IsOpen = running
                && row.Id is not null
                && VsCodeWorkspaces.IsOpen(VsCodeWorkspaces.DefaultStorageRoot, row.Id);

            if (ticks.TryGetValue(row.Uri, out var tick))
            {
                row.Close = tick.Close;
                row.Open = tick.Open;
            }
        }

        return rows;
    }

    /// <summary>Opens each workspace in turn. VS Code focuses one it already has open.</summary>
    public static async Task OpenAsync(IReadOnlyList<(string Uri, string Name)> workspaces, string? vsCodeExecutable, Action<string> log)
    {
        var executable = VsCodeLauncher.ResolveExecutable(vsCodeExecutable);
        if (executable is null)
        {
            log("Could not find VS Code. Set \"VsCodeExecutable\" in the configuration file.");
            return;
        }

        foreach (var (uri, name) in workspaces)
        {
            var (kind, _, _) = VsCodeUri.Describe(uri);
            log($"Opening {name}...");

            try
            {
                Process.Start(VsCodeLauncher.CreateStartInfo(executable, VsCodeLauncher.BuildArguments(uri, kind)));
            }
            catch (Exception ex)
            {
                log($"Failed to open {name}: {ex.Message}");
            }

            // VS Code drops requests that arrive while it is still starting the previous window.
            await Task.Delay(400);
        }
    }

    public static void Launch(WorkspaceRow row, string? vsCodeExecutable, Action<string> log)
    {
        var executable = VsCodeLauncher.ResolveExecutable(vsCodeExecutable);
        if (executable is null)
        {
            log("Could not find VS Code. Set \"VsCodeExecutable\" in the configuration file.");
            return;
        }

        try
        {
            Process.Start(VsCodeLauncher.CreateStartInfo(executable, VsCodeLauncher.BuildArguments(row.Uri, row.Kind)));
            log(row.IsOpen ? $"Focusing {row.Name}..." : $"Launching {row.Name}...");
        }
        catch (Exception ex)
        {
            log($"Failed to launch {row.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks the workspace's window to close. Nothing is ever forced, and nothing is closed unless
    /// exactly one window matches - VS Code shares one process and one AppUserModelID across every
    /// window, so a wrong guess would close somebody else's editor. Returns true once it has closed.
    /// </summary>
    public static async Task<bool> CloseAsync(string name, string? windowTitle, Action<string> log)
    {
        var windows = WindowFinder.FindByProcessName("Code");
        var titles = windows.Select(window => window.Title).ToList();
        var matches = VsCodeWindows.MatchAll(titles, name, windowTitle);

        if (matches.Count == 0)
        {
            log($"{name}: no VS Code window matches. Set \"WindowTitle\" for it in the configuration.");
            return false;
        }

        if (matches.Count > 1)
        {
            var candidates = string.Join(", ", matches.Select(index => $"\"{titles[index]}\""));
            log($"{name}: {matches.Count} windows match ({candidates}); nothing closed. Set \"WindowTitle\" to pin it.");
            return false;
        }

        var target = windows[matches[0]];
        log($"Closing {name} (\"{target.Title}\")...");
        WindowFinder.Close(target.Handle);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!WindowFinder.StillOpen(target.Handle))
            {
                log($"{name}: closed.");
                return true;
            }

            await Task.Delay(150);
        }

        log($"{name}: still open after 3 seconds - VS Code may be asking about unsaved work.");
        return false;
    }

    /// <summary>Preferred is a stated preference, so unlike the ticks it is meant to be saved at once.</summary>
    public static void TogglePreferred(ConfigurationFile config, WorkspaceRow row, Action<string> log)
    {
        var existing = config.Workspaces
            .FirstOrDefault(workspace => string.Equals(workspace.Uri, row.Uri, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            config.Workspaces.Add(new WorkspaceFavorite { Name = row.Name, Uri = row.Uri });
            log($"Added {row.Name} to the preferred workspaces.");
        }
        else
        {
            config.Workspaces.Remove(existing);
            log($"Removed {existing.Name} from the preferred workspaces.");
        }
    }
}
