namespace DevWorkspaceLauncher;

/// <summary>
/// The app's names, in one place. Anything persisted - folders, file names - is built from these,
/// so a rename is a change here plus a migration, not a hunt through the code.
/// </summary>
public static class AppIdentity
{
    /// <summary>Shown to people: window titles, headings.</summary>
    public const string DisplayName = "Developer Workspace Launcher";

    /// <summary>Used wherever a name ends up on disk. Never contains spaces.</summary>
    public const string CodeName = "DevWorkspaceLauncher";

    public const string ConfigFileName = "devworkspacelauncher.json";

    /// <summary>The configuration file name from before the rename, still read once to migrate it.</summary>
    public const string LegacyConfigFileName = "restartapps.json";
}
