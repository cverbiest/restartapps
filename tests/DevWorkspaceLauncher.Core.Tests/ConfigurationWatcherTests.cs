namespace DevWorkspaceLauncher.Tests;

public class ConfigurationWatcherTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("dwl-watch-").FullName;
    private readonly string _path;

    public ConfigurationWatcherTests()
    {
        _path = Path.Combine(_temp, "devworkspacelauncher.json");
        File.WriteAllText(_path, Json("Initial"));
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static string Json(string appName) =>
        $$"""{ "Apps": [ { "Name": "{{appName}}", "Commandline": "x.exe" } ] }""";

    private ConfigurationWatcher Start(out TaskCompletionSource<ConfigurationSnapshot> reloaded)
    {
        var watcher = new ConfigurationWatcher(_path) { LastKnownJson = File.ReadAllText(_path) };
        var completion = new TaskCompletionSource<ConfigurationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Reloaded += (_, snapshot) => completion.TrySetResult(snapshot);
        watcher.Start();
        reloaded = completion;
        return watcher;
    }

    [Fact]
    public async Task Watcher_raises_Reloaded_after_an_external_write()
    {
        using var watcher = Start(out var reloaded);

        File.WriteAllText(_path, Json("Changed"));

        var snapshot = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("Changed", Assert.Single(snapshot.Configuration.Apps).Name);
        Assert.Equal(Json("Changed"), watcher.LastKnownJson);
    }

    [Fact]
    public async Task Watcher_ignores_its_own_write()
    {
        using var watcher = Start(out var reloaded);

        watcher.LastKnownJson = Json("Own");
        File.WriteAllText(_path, Json("Own"));

        await Task.Delay(1500);
        Assert.False(reloaded.Task.IsCompleted);
    }

    [Fact]
    public async Task Two_watchers_one_writer()
    {
        using var writer = Start(out var writerReloaded);
        using var other = Start(out var otherReloaded);

        writer.LastKnownJson = Json("FromWriter");
        File.WriteAllText(_path, Json("FromWriter"));

        var snapshot = await otherReloaded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("FromWriter", Assert.Single(snapshot.Configuration.Apps).Name);
        await Task.Delay(1000);
        Assert.False(writerReloaded.Task.IsCompleted);
    }
}
