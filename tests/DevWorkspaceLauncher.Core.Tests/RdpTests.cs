namespace DevWorkspaceLauncher.Tests;

public class RdpCommandLineTests
{
    [Fact]
    public void Parses_target_credential_display_and_flags()
    {
        var parsed = RdpCommandLine.Parse(["/v:srv01:3390", "/cred:DWL:admin", "/fit:68%", "/mon:2", "/zoom:150", "/admin", "/wait"]);

        Assert.Equal("srv01:3390", parsed.Settings.Host);
        Assert.Equal("DWL:admin", parsed.Settings.Credential);
        Assert.Equal(RdpDisplayMode.Fit, parsed.Settings.Mode);
        Assert.Equal(68, parsed.Settings.Fit);
        Assert.Equal(2, parsed.Settings.Monitor);
        Assert.Equal(150, parsed.Settings.Zoom);
        Assert.True(parsed.Settings.Admin);
        Assert.True(parsed.Wait);
    }

    [Fact]
    public void Fit_without_a_number_uses_the_default()
    {
        Assert.Equal(RdpCommandLine.DefaultFit, RdpCommandLine.Parse(["/fit"]).Settings.Fit);
    }

    [Fact]
    public void First_rdp_argument_is_the_base_file()
    {
        Assert.Equal(@"c:\x\base.rdp", RdpCommandLine.Parse([@"c:\x\base.rdp", "/f"]).Settings.RdpFile);
    }

    [Theory]
    [InlineData("/nonsense")]
    [InlineData("/v")]
    [InlineData("/w:wide")]
    [InlineData("loose")]
    public void Bad_arguments_are_invalid_parameters(string arg)
    {
        var error = Assert.Throws<RdpLaunchException>(() => RdpCommandLine.Parse([arg]));
        Assert.Equal(RdpExitCode.InvalidParameters, error.ExitCode);
    }

    [Fact]
    public void Raw_options_split_on_commas_with_escapes()
    {
        Assert.Equal(
            ["audiomode:i:2", "alternate shell:s:a,b"],
            RdpCommandLine.SplitOptions(@"audiomode:i:2, alternate shell:s:a\,b"));
    }
}

public class RdpResolverTests
{
    private static ConfigurationFile Config() => new()
    {
        RdpSessions =
        [
            new RdpSession { Name = "Build server", Host = "build01", Credential = "DWL:build", Size = "fit68", Monitor = 2 }
        ]
    };

    [Fact]
    public void Session_sits_on_its_preset_and_the_command_line_on_the_session()
    {
        var parsed = RdpCommandLine.Parse(["/session:build SERVER", "/mon:1"]);

        var resolved = RdpResolver.Resolve(Config(), parsed);

        Assert.Equal("build01", resolved.Host);
        Assert.Equal(RdpDisplayMode.Fit, resolved.Mode);
        Assert.Equal(68, resolved.Fit);
        Assert.Equal(150, resolved.Zoom);
        Assert.Equal(1, resolved.Monitor);
    }

    [Fact]
    public void A_size_on_the_command_line_replaces_the_sessions_display()
    {
        var resolved = RdpResolver.Resolve(Config(), RdpCommandLine.Parse(["/session:Build server", "/size:full"]));

        Assert.Equal(RdpDisplayMode.FullScreen, resolved.Mode);
        Assert.Null(resolved.Zoom);
        Assert.Null(resolved.Monitor);
    }

    [Fact]
    public void A_preset_in_the_file_replaces_the_built_in_one()
    {
        var config = Config();
        config.RdpSizes.Add(new RdpSizePreset { Name = "FIT68", Mode = RdpDisplayMode.Max });

        var resolved = RdpResolver.Resolve(config, RdpCommandLine.Parse(["/session:Build server"]));

        Assert.Equal(RdpDisplayMode.Max, resolved.Mode);
        Assert.Equal(["FIT68", "full"], RdpPresets.All(config).Select(preset => preset.Name));
    }

    [Fact]
    public void Unknown_session_is_profile_not_found()
    {
        var error = Assert.Throws<RdpLaunchException>(() => RdpResolver.Resolve(Config(), RdpCommandLine.Parse(["/session:nope"])));
        Assert.Equal(RdpExitCode.ProfileNotFound, error.ExitCode);
    }

    [Fact]
    public void No_host_is_invalid_parameters()
    {
        var error = Assert.Throws<RdpLaunchException>(() => RdpResolver.Resolve(Config(), RdpCommandLine.Parse(["/f"])));
        Assert.Equal(RdpExitCode.InvalidParameters, error.ExitCode);
    }

    [Theory]
    [InlineData("srv", "srv", 3389)]
    [InlineData("srv:3390", "srv", 3390)]
    [InlineData("10.0.0.5:4000", "10.0.0.5", 4000)]
    [InlineData("fe80::1", "fe80::1", 3389)]
    public void SplitHost(string input, string host, int port)
    {
        Assert.Equal((host, port), RdpResolver.SplitHost(input));
    }
}

public class RdpLayoutTests
{
    // A 1920x1080 monitor to the right of the primary one, with a 40 px taskbar at the bottom.
    private static readonly MonitorInfo Second = new(
        new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1040), Primary: false, Dpi: 96);

    private static readonly MonitorInfo Primary = new(
        new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), Primary: true, Dpi: 96);

    private static readonly WindowChrome Chrome = new(Horizontal: 8, Caption: 31, Bottom: 8);

    [Fact]
    public void Full_screen_covers_the_whole_monitor()
    {
        var geometry = RdpLayout.Compute(new RdpDisplay { Mode = RdpDisplayMode.FullScreen }, Second, Chrome);

        Assert.Equal(new RdpGeometry(2, 1920, 1080, Second.Bounds), geometry);
    }

    [Fact]
    public void Max_fills_the_working_area_less_the_frame()
    {
        var geometry = RdpLayout.Compute(new RdpDisplay { Mode = RdpDisplayMode.Max }, Second, Chrome);

        Assert.Equal(new RdpGeometry(1, 1904, 1001, Second.WorkingArea), geometry);
    }

    [Fact]
    public void Fit_percentage_is_centred_in_the_working_area()
    {
        var geometry = RdpLayout.Compute(new RdpDisplay { Mode = RdpDisplayMode.Fit, Fit = 50 }, Second, Chrome);

        Assert.Equal(new PixelRect(2400, 260, 3360, 780), geometry.Window);
        Assert.Equal((944, 481), (geometry.DesktopWidth, geometry.DesktopHeight));
    }

    [Fact]
    public void Fit_above_100_is_a_pixel_margin()
    {
        var geometry = RdpLayout.Compute(new RdpDisplay { Mode = RdpDisplayMode.Fit, Fit = 120 }, Primary, Chrome);

        Assert.Equal(new PixelRect(120, 120, 1800, 920), geometry.Window);
    }

    [Fact]
    public void Fixed_size_is_the_client_area()
    {
        var geometry = RdpLayout.Compute(new RdpDisplay { Mode = RdpDisplayMode.Size, Width = 1280, Height = 720 }, Primary, Chrome);

        Assert.Equal((1280, 720), (geometry.DesktopWidth, geometry.DesktopHeight));
        Assert.Equal(1280 + 16, geometry.Window.Width);
        Assert.Equal(720 + 39, geometry.Window.Height);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(null, true)]
    public void Monitor_past_the_last_falls_back_to_the_primary(int? number, bool primary)
    {
        Assert.Equal(primary, RdpLayout.PickMonitor([Primary, Second], number).Primary);
    }
}

public class RdpFileWriterTests
{
    private static readonly RdpGeometry Geometry = new(1, 1280, 720, new PixelRect(10, 20, 1306, 779));

    [Fact]
    public void Writes_the_explicit_settings_and_never_a_password()
    {
        var settings = new RdpSettings { Host = "srv:3390", Username = "carl", Domain = "CCE", Zoom = 150 };

        var lines = RdpFileWriter.Build(settings, Geometry).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("full address:s:srv:3390", lines);
        Assert.Contains("username:s:CCE\\carl", lines);
        Assert.Contains("winposstr:s:0,1,10,20,1306,779", lines);
        Assert.Contains("desktopscalefactor:i:150", lines);
        Assert.Contains("smart sizing:i:0", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Base_file_then_raw_options_then_own_settings_win()
    {
        var settings = new RdpSettings { Host = "srv", Options = ["audiomode:i:2", "desktopwidth:i:99"] };
        const string baseFile = "audiomode:i:0\r\nredirectprinters:i:1\r\nnot a setting\r\n";

        var lines = RdpFileWriter.Build(settings, Geometry, baseFile).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("redirectprinters:i:1", lines);
        Assert.Contains("audiomode:i:2", lines);
        Assert.DoesNotContain("audiomode:i:0", lines);
        Assert.Contains("desktopwidth:i:1280", lines);
        Assert.DoesNotContain("desktopwidth:i:99", lines);
        Assert.DoesNotContain("not a setting", lines);
    }

    [Fact]
    public void Configuration_round_trips_sessions_without_nulls()
    {
        var config = new ConfigurationFile
        {
            RdpSessions = [new RdpSession { Name = "S", Host = "h", Size = "fit68" }]
        };

        var json = AppConfigurationStore.ToJson(config);
        var session = Assert.Single(AppConfigurationStore.Parse(json).RdpSessions);

        Assert.DoesNotContain("\"Credential\"", json);
        Assert.Equal(("S", "h", "fit68"), (session.Name, session.Host, session.Size));
    }
}
