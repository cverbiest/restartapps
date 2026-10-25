namespace DevWorkspaceLauncher.Tests;

public class UserEnvironmentTests
{
    [Fact]
    public void Parses_name_value_pairs_from_an_environment_block()
    {
        var variables = UserEnvironment.ParseBlock("Path=C:\\a;C:\\b\0TEMP=C:\\Temp\0\0");

        Assert.Equal(2, variables.Count);
        Assert.Equal("C:\\a;C:\\b", variables["Path"]);
        Assert.Equal("C:\\Temp", variables["TEMP"]);
    }

    [Fact]
    public void Keeps_equals_signs_inside_values()
    {
        var variables = UserEnvironment.ParseBlock("OPTS=a=b\0\0");

        Assert.Equal("a=b", variables["OPTS"]);
    }

    [Fact]
    public void Skips_hidden_drive_variables()
    {
        var variables = UserEnvironment.ParseBlock("=C:=C:\\work\0Path=C:\\a\0\0");

        Assert.Single(variables);
        Assert.Equal("C:\\a", variables["Path"]);
    }

    [Fact]
    public void Reads_the_current_environment_from_the_registry()
    {
        var variables = UserEnvironment.Load();

        Assert.True(variables.ContainsKey("Path"));
        Assert.True(variables.ContainsKey("SystemRoot"));
    }
}
