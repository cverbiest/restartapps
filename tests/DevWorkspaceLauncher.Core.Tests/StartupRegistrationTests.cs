namespace DevWorkspaceLauncher.Tests;

public class StartupRegistrationTests
{
    private const string Exe = @"C:\Apps\Launcher\DevWorkspaceLauncher.WinUI.exe";

    [Theory]
    [InlineData(null, StartupMode.Off)]
    [InlineData("", StartupMode.Off)]
    [InlineData("\"" + Exe + "\" /tray", StartupMode.Tray)]
    [InlineData("\"" + Exe + "\" /TRAY", StartupMode.Tray)]
    [InlineData("\"" + Exe + "\"", StartupMode.Window)]
    [InlineData("\"" + Exe + "\" ", StartupMode.Window)]
    [InlineData("\"c:\\apps\\launcher\\devworkspacelauncher.winui.exe\" /tray", StartupMode.Tray)]
    public void Mode_is_read_from_the_command(string? command, StartupMode expected) =>
        Assert.Equal(expected, StartupRegistration.ModeOf(command, Exe));

    [Fact]
    public void A_command_for_another_copy_is_off_for_this_one() =>
        Assert.Equal(StartupMode.Off, StartupRegistration.ModeOf(@"""D:\Other\DevWorkspaceLauncher.WinUI.exe"" /tray", Exe));

    [Theory]
    [InlineData(StartupMode.Tray, "\"" + Exe + "\" /tray")]
    [InlineData(StartupMode.Window, "\"" + Exe + "\"")]
    public void Command_starts_the_executable_in_the_mode(StartupMode mode, string expected) =>
        Assert.Equal(expected, StartupRegistration.CommandFor(mode, Exe));

    [Theory]
    [InlineData(null, null)]
    [InlineData("\"" + Exe + "\" /tray", null)]
    [InlineData(@"""D:\Other\DevWorkspaceLauncher.WinUI.exe"" /tray", @"D:\Other\DevWorkspaceLauncher.WinUI.exe")]
    [InlineData(@"D:\Other\launcher.exe /tray", @"D:\Other\launcher.exe")]
    public void Other_executable_is_the_copy_the_command_starts_when_it_is_not_this_one(string? command, string? expected) =>
        Assert.Equal(expected, StartupRegistration.OtherExecutableOf(command, Exe));
}
