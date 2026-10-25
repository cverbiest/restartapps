namespace DevWorkspaceLauncher.Tests;

public class VsCodeOpenProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "restartapps-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    private string WriteDatabase(string id)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "state.vscdb");
        File.WriteAllText(file, "stands in for the workspace state database");
        return file;
    }

    [Fact]
    public void Reports_not_open_when_the_database_is_missing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        Assert.False(VsCodeWorkspaces.IsOpen(_root, "empty"));
    }

    [Fact]
    public void Reports_not_open_when_nothing_holds_the_database()
    {
        WriteDatabase("closed");

        Assert.False(VsCodeWorkspaces.IsOpen(_root, "closed"));
    }

    [Fact]
    public void Reports_open_while_another_handle_holds_the_database()
    {
        var file = WriteDatabase("live");

        // This is how SQLite - and so a live VS Code window - holds the file: readable and writable
        // by others, which still defeats an exclusive open.
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            Assert.True(VsCodeWorkspaces.IsOpen(_root, "live"));
        }

        Assert.False(VsCodeWorkspaces.IsOpen(_root, "live"));
    }

    [Fact]
    public void Reports_not_open_for_an_unknown_id()
    {
        Assert.False(VsCodeWorkspaces.IsOpen(_root, "no-such-id"));
    }
}
