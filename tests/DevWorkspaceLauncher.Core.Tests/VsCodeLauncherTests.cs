namespace DevWorkspaceLauncher.Tests;

public class VsCodeLauncherTests
{
    [Fact]
    public void Opens_a_folder_with_folder_uri()
    {
        Assert.Equal(
            "--folder-uri \"file:///c%3A/code/alpha\"",
            VsCodeLauncher.BuildArguments("file:///c%3A/code/alpha", WorkspaceKind.Folder));
    }

    [Fact]
    public void Opens_a_workspace_file_with_file_uri()
    {
        Assert.Equal(
            "--file-uri \"file:///c%3A/code/beta/beta.code-workspace\"",
            VsCodeLauncher.BuildArguments("file:///c%3A/code/beta/beta.code-workspace", WorkspaceKind.WorkspaceFile));
    }

    [Fact]
    public void Prefers_the_configured_executable()
    {
        Assert.Equal(@"C:\tools\Code.exe", VsCodeLauncher.ResolveExecutable(@"C:\tools\Code.exe", _ => true));
    }

    [Fact]
    public void Expands_environment_variables_in_the_configured_executable()
    {
        var resolved = VsCodeLauncher.ResolveExecutable(@"%SystemRoot%\Code.exe", _ => true);

        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "Code.exe"), resolved);
    }

    [Fact]
    public void Falls_back_to_the_per_user_install_when_nothing_is_configured()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "Code.exe");

        Assert.Equal(expected, VsCodeLauncher.ResolveExecutable(null, path => path == expected));
    }

    [Fact]
    public void Skips_a_configured_executable_that_is_not_there()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft VS Code",
            "Code.exe");

        Assert.Equal(expected, VsCodeLauncher.ResolveExecutable(@"C:\gone\Code.exe", path => path == expected));
    }

    [Fact]
    public void Looks_for_the_cli_shim_on_the_path()
    {
        Assert.Contains(
            VsCodeLauncher.CandidatePaths(null),
            candidate => candidate.EndsWith("code.cmd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Returns_null_when_no_candidate_exists()
    {
        Assert.Null(VsCodeLauncher.ResolveExecutable(null, _ => false));
    }

    [Fact]
    public void Starts_an_exe_directly()
    {
        var info = VsCodeLauncher.CreateStartInfo(@"C:\tools\Code.exe", "--folder-uri \"x\"");

        Assert.Equal(@"C:\tools\Code.exe", info.FileName);
        Assert.Equal("--folder-uri \"x\"", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
    }

    [Fact]
    public void Starts_a_cmd_shim_through_the_command_processor()
    {
        var info = VsCodeLauncher.CreateStartInfo(@"C:\tools\code.cmd", "--folder-uri \"x\"");

        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal("/c \"\"C:\\tools\\code.cmd\" --folder-uri \"x\"\"", info.Arguments);
        Assert.True(info.CreateNoWindow);
    }
}
