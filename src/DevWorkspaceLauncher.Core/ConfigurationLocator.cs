namespace DevWorkspaceLauncher;

/// <summary>Where the configuration lives, and the pre-rename file to copy there first, if any.</summary>
public sealed record ConfigurationLocation(string Path, string? MigrateFrom);

/// <summary>
/// Decides which configuration file to use.
/// <para>
/// Both apps must read the same file, but they build into different folders, so the default is a
/// shared file under %APPDATA%. A file next to the executable still wins, for a portable copy. A
/// <see cref="AppIdentity.LegacyConfigFileName"/> from before the rename is migrated into the shared
/// location, but never over a shared file that already exists.
/// </para>
/// </summary>
public static class ConfigurationLocator
{
    public static string SharedDirectory(string appDataDirectory) =>
        Path.Combine(appDataDirectory, AppIdentity.CodeName);

    public static ConfigurationLocation Resolve(
        string executableDirectory,
        string currentDirectory,
        string appDataDirectory,
        Func<string, bool> fileExists)
    {
        var portable = Path.Combine(executableDirectory, AppIdentity.ConfigFileName);
        if (fileExists(portable))
        {
            return new ConfigurationLocation(portable, null);
        }

        var shared = Path.Combine(SharedDirectory(appDataDirectory), AppIdentity.ConfigFileName);
        if (fileExists(shared))
        {
            return new ConfigurationLocation(shared, null);
        }

        var legacy = new[] { executableDirectory, currentDirectory }
            .Select(directory => Path.Combine(directory, AppIdentity.LegacyConfigFileName))
            .FirstOrDefault(fileExists);

        return new ConfigurationLocation(shared, legacy);
    }

    /// <summary>The location for this process: its executable, its current directory and %APPDATA%.</summary>
    public static ConfigurationLocation ResolveForCurrentProcess() =>
        Resolve(
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            File.Exists);
}
