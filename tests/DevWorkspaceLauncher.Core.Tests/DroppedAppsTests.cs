namespace DevWorkspaceLauncher.Tests;

public class DroppedAppsTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("dwl-drop-").FullName;
    private readonly List<string> _log = [];

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string CreateFile(string name)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void Dropped_exe_becomes_an_entry_with_both_ticks()
    {
        var path = CreateFile("tool.exe");
        var config = new ConfigurationFile();

        var added = DroppedApps.Add(config, [path], _log.Add);

        var app = Assert.Single(added);
        Assert.Same(app, Assert.Single(config.Apps));
        Assert.Equal("tool", app.Name);
        Assert.Equal($"\"{path}\"", app.Commandline);
        Assert.True(app.Kill);
        Assert.True(app.Restart);
        Assert.Contains($"Added tool: \"{path}\"", _log);
    }

    [Fact]
    public void Dropped_duplicate_name_is_skipped_and_logged()
    {
        var path = CreateFile("tool.exe");
        var config = new ConfigurationFile { Apps = [new AppConfiguration { Name = "TOOL", Commandline = "x" }] };

        var added = DroppedApps.Add(config, [path, path], _log.Add);

        Assert.Empty(added);
        Assert.Single(config.Apps);
        Assert.Contains("tool is already in the configuration.", _log);
    }

    [Fact]
    public void Dropped_folder_and_missing_file_are_ignored()
    {
        var missing = Path.Combine(_temp, "gone.exe");
        var config = new ConfigurationFile();

        var added = DroppedApps.Add(config, [_temp, missing], _log.Add);

        Assert.Empty(added);
        Assert.Empty(config.Apps);
        Assert.Equal([$"Ignoring folder: {_temp}", $"Ignoring missing file: {missing}"], _log);
    }
}
