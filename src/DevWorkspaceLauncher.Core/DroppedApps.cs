namespace DevWorkspaceLauncher;

/// <summary>Turns executables and shortcuts dropped on the window into configured apps.</summary>
public static class DroppedApps
{
    /// <summary>
    /// Appends an entry per dropped file to <paramref name="config"/>, skipping folders, missing files and
    /// names already present. Returns the added entries; the caller saves.
    /// </summary>
    public static List<AppConfiguration> Add(ConfigurationFile config, IEnumerable<string> paths, Action<string> log)
    {
        var added = new List<AppConfiguration>();

        foreach (var path in paths)
        {
            var app = CreateEntry(path, log);
            if (app is null)
            {
                continue;
            }

            var isDuplicate = config.Apps.Concat(added).Any(existing =>
                string.Equals(existing.Name, app.Name, StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
            {
                log($"{app.Name} is already in the configuration.");
                continue;
            }

            added.Add(app);
            log($"Added {app.Name}: {app.Commandline}");
        }

        config.Apps.AddRange(added);
        return added;
    }

    private static AppConfiguration? CreateEntry(string path, Action<string> log)
    {
        if (Directory.Exists(path))
        {
            log($"Ignoring folder: {path}");
            return null;
        }

        if (!File.Exists(path))
        {
            log($"Ignoring missing file: {path}");
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var commandline = $"\"{path}\"";

        if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, arguments) = ShortcutResolver.Resolve(path);
            if (string.IsNullOrWhiteSpace(target))
            {
                log($"Could not resolve shortcut {Path.GetFileName(path)}; using the shortcut itself.");
            }
            else
            {
                commandline = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{target}\""
                    : $"\"{target}\" {arguments.Trim()}";
            }
        }

        return new AppConfiguration
        {
            Name = name,
            Commandline = commandline,
            Kill = true,
            Restart = true
        };
    }
}
