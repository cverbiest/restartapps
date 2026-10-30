namespace DevWorkspaceLauncher.Tests;

public class SshConfigTests
{
    private const string Sample =
        "# my hosts\n" +
        "Include extra.conf\n" +
        "\n" +
        "Host dev03 dev03.cce.local\n" +
        "  HostName cce-dev03\n" +
        "  user cvb\n" +
        "  IdentityFile ~/.ssh/id_dev\n" +
        "  IdentityFile ~/.ssh/id_old\n" +
        "  ForwardAgent yes\n" +
        "\n" +
        "# the build box\n" +
        "Host=build\n" +
        "    HostName=\"build.cce.be\"\n" +
        "\n" +
        "Host *\n" +
        "  ServerAliveInterval 30\n";

    private static SshHostFields Fields(SshHostEntry entry) => new(entry.Alias, entry.HostName, entry.User, entry.IdentityFile);

    [Fact]
    public void Reads_the_hosts_but_not_wildcard_entries()
    {
        var hosts = SshConfigDocument.Parse(Sample).Hosts;

        Assert.Equal(["dev03", "build"], hosts.Select(host => host.Alias));
        Assert.Equal(["dev03.cce.local"], hosts[0].OtherPatterns);
        Assert.Equal("cce-dev03", hosts[0].HostName);
        Assert.Equal("cvb", hosts[0].User);
        Assert.Equal("~/.ssh/id_dev", hosts[0].IdentityFile);
        Assert.Equal(["~/.ssh/id_old"], hosts[0].MoreIdentityFiles);
        Assert.Equal("build.cce.be", hosts[1].HostName);
        Assert.Null(hosts[1].User);
    }

    [Theory]
    [InlineData(Sample)]
    [InlineData("Host a\r\n  HostName x\r\n# no newline at the end")]
    [InlineData("")]
    public void Writes_back_an_unchanged_file_exactly(string text)
    {
        Assert.Equal(text, SshConfigDocument.Parse(text).ToText());
    }

    [Fact]
    public void Edit_changes_only_the_value_and_keeps_the_rest_of_the_line()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Update("dev03", Fields(document.Hosts[0]) with { User = "root" });

        Assert.Equal(Sample.Replace("  user cvb\n", "  user root\n"), document.ToText());
    }

    [Fact]
    public void Edit_keeps_the_equals_form_and_the_quotes()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Update("build", Fields(document.Hosts[1]) with { HostName = "other.cce.be" });

        Assert.Contains("\nHost=build\n    HostName=\"other.cce.be\"\n\n", document.ToText());
    }

    [Fact]
    public void A_value_with_spaces_is_quoted()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Update("build", Fields(document.Hosts[1]) with { IdentityFile = @"C:\Users\c v b\.ssh\id" });

        Assert.Contains("    HostName=\"build.cce.be\"\n    IdentityFile \"C:\\Users\\c v b\\.ssh\\id\"\n\nHost *", document.ToText());
    }

    [Fact]
    public void A_missing_keyword_goes_after_the_last_setting_with_the_entry_indentation()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Update("build", Fields(document.Hosts[1]) with { User = "jenkins" });

        Assert.Contains("    HostName=\"build.cce.be\"\n    User jenkins\n\nHost *", document.ToText());
    }

    [Fact]
    public void Clearing_a_field_removes_its_first_line_only()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Update("dev03", Fields(document.Hosts[0]) with { IdentityFile = null });

        var text = document.ToText();
        Assert.DoesNotContain("id_dev", text);
        Assert.Contains("  IdentityFile ~/.ssh/id_old\n", text);
    }

    [Fact]
    public void Renaming_keeps_the_other_patterns()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Update("dev03", Fields(document.Hosts[0]) with { Alias = "dev3" });

        Assert.Contains("\nHost dev3 dev03.cce.local\n", document.ToText());
    }

    [Fact]
    public void Add_goes_before_the_trailing_defaults_indented_like_the_file()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Add(new SshHostFields("new", "new.cce.be", "cvb", null));

        Assert.EndsWith(
            "    HostName=\"build.cce.be\"\n\nHost new\n  HostName new.cce.be\n  User cvb\n\nHost *\n  ServerAliveInterval 30\n",
            document.ToText());
    }

    [Fact]
    public void Remove_the_last_entry_leaves_no_blank_line_behind()
    {
        var document = SshConfigDocument.Parse("Host a\n  HostName x\n\nHost b\n  HostName y\n");

        document.Remove("b");

        Assert.Equal("Host a\n  HostName x\n", document.ToText());
    }

    [Fact]
    public void Add_to_an_empty_file()
    {
        var document = SshConfigDocument.Parse(string.Empty);

        document.Add(new SshHostFields("a", "a.example", null, null));

        Assert.Equal("Host a\n    HostName a.example\n", document.ToText());
    }

    [Fact]
    public void Add_keeps_the_line_endings()
    {
        var document = SshConfigDocument.Parse("Host a\r\n  HostName x\r\n");

        document.Add(new SshHostFields("b", "y", null, null));

        Assert.Equal("Host a\r\n  HostName x\r\n\r\nHost b\r\n  HostName y\r\n", document.ToText());
    }

    [Fact]
    public void Remove_takes_the_entry_with_its_comment_and_leaves_the_others()
    {
        var document = SshConfigDocument.Parse(Sample);

        Assert.True(document.Remove("build"));

        Assert.Equal(Sample.Replace("# the build box\nHost=build\n    HostName=\"build.cce.be\"\n\n", string.Empty), document.ToText());
    }

    [Fact]
    public void Remove_the_first_entry_keeps_the_header_comment()
    {
        var document = SshConfigDocument.Parse(Sample);

        document.Remove("dev03");

        Assert.StartsWith("# my hosts\nInclude extra.conf\n\n# the build box\nHost=build\n", document.ToText());
    }

    [Fact]
    public void Save_refuses_a_file_changed_since_it_was_read_and_keeps_line_endings()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, ".ssh", "config");
            SshConfigStore.Save(path, string.Empty, "Host a\r\n");
            var snapshot = SshConfigStore.Load(path);
            Assert.Equal("Host a\r\n", snapshot.Text);

            File.WriteAllText(path, "Host b\r\n");
            Assert.Throws<SshConfigChangedException>(() => SshConfigStore.Save(path, snapshot.Text, "Host c\r\n"));
            Assert.Equal("Host b\r\n", File.ReadAllText(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Pinning_a_host_adds_its_settings_and_unpinning_prunes_them()
    {
        var config = new ConfigurationFile();
        var host = new SshHost(SshConfigDocument.Parse(Sample).Hosts[0], config);

        FavoriteOrdering.Toggle(host);
        Assert.Equal("dev03", Assert.Single(config.SshHosts).Alias);
        Assert.True(new SshHost(host.Entry, config).Favorite);

        FavoriteOrdering.Toggle(host);
        SshHost.Prune(config);
        Assert.Empty(config.SshHosts);
    }

    [Fact]
    public void Renaming_follows_in_activities_and_settings()
    {
        var config = new ConfigurationFile
        {
            SshHosts = [new SshHostSettings { Alias = "dev03", Favorite = true }],
            Activities = [new Activity { Name = "X", SshHosts = [new ActivitySshHost { Name = "DEV03" }] }]
        };

        ActivityOperations.RenameSshHost(config, "dev03", "dev3");

        Assert.Equal("dev3", config.SshHosts[0].Alias);
        Assert.Equal("dev3", config.Activities[0].SshHosts[0].Name);

        ActivityOperations.RemoveSshHost(config, "dev3");

        Assert.Empty(config.SshHosts);
        Assert.Empty(config.Activities[0].SshHosts);
    }

    [Fact]
    public void The_client_setting_reads_any_case_and_is_left_out_when_automatic()
    {
        Assert.Equal(SshClient.WindowsTerminal, AppConfigurationStore.Parse("""{ "SshClient": "windowsterminal" }""").SshClient);
        Assert.DoesNotContain("SshClient", AppConfigurationStore.ToJson(new ConfigurationFile()));
        Assert.Contains("\"SshClient\": \"WezTerm\"", AppConfigurationStore.ToJson(new ConfigurationFile { SshClient = SshClient.WezTerm }));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("dev*", false)]
    [InlineData("!dev", false)]
    [InlineData("cce-dev03_cvb", true)]
    public void Alias_rules(string alias, bool valid)
    {
        Assert.Equal(valid, SshConfigDocument.IsValidAlias(alias));
    }
}
