namespace DevWorkspaceLauncher.Tests;

public class VsCodeUriTests
{
    [Fact]
    public void Describes_a_local_folder()
    {
        var (kind, name, location) = VsCodeUri.Describe("file:///c%3A/cce/workspaces/restartapps");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("restartapps", name);
        Assert.Equal(@"c:\cce\workspaces\restartapps", location);
    }

    [Fact]
    public void Describes_a_local_workspace_file()
    {
        var (kind, name, location) = VsCodeUri.Describe("file:///c%3A/cce/workspaces/develop/develop.code-workspace");

        Assert.Equal(WorkspaceKind.WorkspaceFile, kind);
        Assert.Equal("develop", name);
        Assert.Equal(@"c:\cce\workspaces\develop\develop.code-workspace", location);
    }

    [Fact]
    public void Describes_an_ssh_remote_workspace_file()
    {
        var (kind, name, location) = VsCodeUri.Describe(
            "vscode-remote://ssh-remote%2Bcce-dev03_cvb/home/cce/cvb/workspaces/develop/develop.code-workspace");

        Assert.Equal(WorkspaceKind.WorkspaceFile, kind);
        Assert.Equal("develop", name);
        Assert.Equal("SSH: cce-dev03_cvb", location);
    }

    [Fact]
    public void Describes_a_wsl_folder()
    {
        var (kind, name, location) = VsCodeUri.Describe("vscode-remote://wsl%2BUbuntu/home/cvb/code/b1-cce-poc");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("b1-cce-poc", name);
        Assert.Equal("WSL: Ubuntu", location);
    }

    [Fact]
    public void Describes_a_dev_container_folder_without_showing_its_encoded_authority()
    {
        var (kind, name, location) = VsCodeUri.Describe(
            "vscode-remote://dev-container%2B7b22686f737450617468223a222782f79227d/workspaces/b1-cce-poc");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("b1-cce-poc", name);
        Assert.Equal("Dev Container", location);
    }

    [Fact]
    public void Ignores_a_trailing_slash_on_a_folder()
    {
        var (_, name, _) = VsCodeUri.Describe("file:///c%3A/cce/workspaces/restartapps/");

        Assert.Equal("restartapps", name);
    }

    [Fact]
    public void Falls_back_to_the_raw_authority_for_an_unknown_remote()
    {
        var (_, _, location) = VsCodeUri.Describe("vscode-remote://codespaces%2Bfoo/workspaces/bar");

        Assert.Equal("codespaces+foo", location);
    }

    [Fact]
    public void Survives_a_uri_it_cannot_parse()
    {
        var (kind, name, location) = VsCodeUri.Describe("not-a-uri");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("not-a-uri", name);
        Assert.Equal("not-a-uri", location);
    }

    [Theory]
    [InlineData("file:///c%3A/cce/workspaces/develop/develop.code-workspace", "develop", "develop")]
    [InlineData("vscode-remote://ssh-remote%2Bcce-dev03/home/cvb/develop.code-workspace", "develop", "develop (cce-dev03)")]
    [InlineData("vscode-remote://wsl%2BUbuntu/home/cvb/code/b1", "b1", "b1 (Ubuntu)")]
    [InlineData("vscode-remote://ssh-remote%2Bcce-dev03/home/cvb/develop", "develop (cce-dev03)", "develop (cce-dev03)")]
    public void Title_adds_the_remote_host_unless_the_name_has_it(string uri, string name, string expected)
    {
        Assert.Equal(expected, VsCodeUri.Title(name, uri));
    }
}
