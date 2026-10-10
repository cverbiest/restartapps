namespace RestartApps.Tests;

public class VsCodeWorkspacesTests : IDisposable
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

    /// <summary>Writes one workspaceStorage entry and stamps the directory so ordering is deterministic.</summary>
    private void WriteEntry(string id, string json, DateTime lastUsedUtc)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "workspace.json"), json);
        Directory.SetLastWriteTimeUtc(directory, lastUsedUtc);
    }

    [Fact]
    public void Returns_nothing_when_the_storage_root_does_not_exist()
    {
        Assert.Empty(VsCodeWorkspaces.Discover(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Reads_folder_and_workspace_entries()
    {
        WriteEntry("aaa", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("bbb", """{ "workspace": "file:///c%3A/code/beta/beta.code-workspace" }""", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var workspaces = VsCodeWorkspaces.Discover(_root);

        Assert.Equal(2, workspaces.Count);

        // Most recently used first.
        Assert.Equal("beta", workspaces[0].DisplayName);
        Assert.Equal("bbb", workspaces[0].Id);
        Assert.Equal(WorkspaceKind.WorkspaceFile, workspaces[0].Kind);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), workspaces[0].LastUsed);

        Assert.Equal("alpha", workspaces[1].DisplayName);
        Assert.Equal(WorkspaceKind.Folder, workspaces[1].Kind);
    }

    [Fact]
    public void Keeps_the_newest_of_two_entries_for_the_same_uri()
    {
        WriteEntry("old", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("new", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        var workspace = Assert.Single(VsCodeWorkspaces.Discover(_root));

        Assert.Equal("new", workspace.Id);
    }

    [Fact]
    public void Skips_entries_it_cannot_use()
    {
        Directory.CreateDirectory(Path.Combine(_root, "no-json"));
        WriteEntry("broken", "{ not json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("empty-uri", """{ "folder": "" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("wrong-shape", """{ "folder": 42 }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("good", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var workspace = Assert.Single(VsCodeWorkspaces.Discover(_root));

        Assert.Equal("good", workspace.Id);
    }

    [Fact]
    public void Points_at_the_real_vs_code_storage_by_default()
    {
        Assert.EndsWith(Path.Combine("Code", "User", "workspaceStorage"), VsCodeWorkspaces.DefaultStorageRoot);
    }
}
