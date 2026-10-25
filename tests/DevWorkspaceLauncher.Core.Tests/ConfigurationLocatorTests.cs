namespace DevWorkspaceLauncher.Tests;

public class ConfigurationLocatorTests : IDisposable
{
    private const string Exe = @"C:\exe";
    private const string Cwd = @"C:\cwd";
    private const string AppData = @"C:\appdata";
    private const string Shared = @"C:\appdata\DevWorkspaceLauncher\devworkspacelauncher.json";

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "dwl-locator-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private static ConfigurationLocation Resolve(params string[] existing)
    {
        var files = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return ConfigurationLocator.Resolve(Exe, Cwd, AppData, files.Contains);
    }

    [Fact]
    public void Portable_file_next_to_the_exe_wins()
    {
        var location = Resolve(@"C:\exe\devworkspacelauncher.json", Shared);

        Assert.Equal(@"C:\exe\devworkspacelauncher.json", location.Path);
        Assert.Null(location.MigrateFrom);
    }

    [Fact]
    public void Shared_file_is_the_default()
    {
        var location = Resolve(Shared);

        Assert.Equal(Shared, location.Path);
        Assert.Null(location.MigrateFrom);
    }

    [Fact]
    public void Legacy_file_next_to_the_exe_is_migrated()
    {
        var location = Resolve(@"C:\exe\restartapps.json");

        Assert.Equal(Shared, location.Path);
        Assert.Equal(@"C:\exe\restartapps.json", location.MigrateFrom);
    }

    [Fact]
    public void Legacy_file_in_the_current_directory_is_migrated()
    {
        var location = Resolve(@"C:\cwd\restartapps.json");

        Assert.Equal(Shared, location.Path);
        Assert.Equal(@"C:\cwd\restartapps.json", location.MigrateFrom);
    }

    [Fact]
    public void Existing_shared_file_is_never_replaced_by_a_legacy_one()
    {
        var location = Resolve(Shared, @"C:\exe\restartapps.json");

        Assert.Equal(Shared, location.Path);
        Assert.Null(location.MigrateFrom);
    }

    [Fact]
    public void Nothing_anywhere_means_a_new_shared_file()
    {
        var location = Resolve();

        Assert.Equal(Shared, location.Path);
        Assert.Null(location.MigrateFrom);
    }

    [Fact]
    public void EnsureExists_copies_and_keeps_the_legacy_file()
    {
        Directory.CreateDirectory(_temp);
        var legacy = Path.Combine(_temp, "restartapps.json");
        File.WriteAllText(legacy, """{ "Apps": [ { "Name": "A", "Commandline": "a.exe" } ] }""");
        var target = Path.Combine(_temp, "shared", "devworkspacelauncher.json");

        var message = AppConfigurationStore.EnsureExists(new ConfigurationLocation(target, legacy));

        Assert.Equal($"Migrated configuration from {legacy} to {target}.", message);
        Assert.Equal(File.ReadAllText(legacy), File.ReadAllText(target));
        Assert.True(File.Exists(legacy));
        Assert.Null(AppConfigurationStore.EnsureExists(new ConfigurationLocation(target, legacy)));
    }

    [Fact]
    public void EnsureExists_creates_an_empty_configuration()
    {
        var target = Path.Combine(_temp, "shared", "devworkspacelauncher.json");

        var message = AppConfigurationStore.EnsureExists(new ConfigurationLocation(target, null));

        Assert.Equal($"Created a new configuration at {target}.", message);
        Assert.Empty(AppConfigurationStore.Parse(File.ReadAllText(target)).Apps);
    }
}
