using System.Net.NetworkInformation;

namespace DevWorkspaceLauncher.Tests;

public class NetworksTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, "TPLink network to Linksys", "TP-LINK Gigabit Ethernet USB Adapter", NetworkKind.Wired)]
    [InlineData(NetworkInterfaceType.Wireless80211, "Wi-Fi 2", "TP-Link Wireless USB Adapter", NetworkKind.WiFi)]
    [InlineData(NetworkInterfaceType.Wwanpp, "Cellular", "Mobile Broadband", NetworkKind.Mobile)]
    [InlineData(NetworkInterfaceType.Ppp, "contoso-vpn", "contoso-vpn", NetworkKind.Vpn)]
    [InlineData(NetworkInterfaceType.Ethernet, "Ugreen USB Adapter", "TAP-Windows Adapter V9", NetworkKind.Vpn)]
    [InlineData((NetworkInterfaceType)53, "Local Area Connection", "TAP Adapter OAS NDIS 6.0", NetworkKind.Vpn)]
    [InlineData(NetworkInterfaceType.Ethernet, "wg0", "WireGuard Tunnel", NetworkKind.Vpn)]
    public void Classifies_by_type_and_adapter(NetworkInterfaceType type, string name, string description, NetworkKind expected)
    {
        Assert.Equal(expected, NetworkClassifier.Classify(type, OperationalStatus.Up, name, description));
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, "vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData(NetworkInterfaceType.Ethernet, "vSwitch (Local0)", "Hyper-V Virtual Switch Extension Adapter #2")]
    [InlineData(NetworkInterfaceType.Ethernet, "TPLink network to Linksys-QoS Packet Scheduler-0000", "TP-LINK Gigabit Ethernet USB Adapter-QoS Packet Scheduler-0000")]
    [InlineData(NetworkInterfaceType.Ethernet, "Local Area Connection* 6", "WAN Miniport (IP)")]
    [InlineData(NetworkInterfaceType.Wireless80211, "Local Area Connection* 11", "Microsoft Wi-Fi Direct Virtual Adapter #3")]
    [InlineData(NetworkInterfaceType.Tunnel, "Teredo Tunneling Pseudo-Interface", "Microsoft Teredo Tunneling Adapter")]
    [InlineData(NetworkInterfaceType.Loopback, "Loopback Pseudo-Interface 1", "Software Loopback Interface 1")]
    public void Plumbing_is_not_shown(NetworkInterfaceType type, string name, string description)
    {
        Assert.Null(NetworkClassifier.Classify(type, OperationalStatus.Up, name, description));
    }

    [Fact]
    public void An_adapter_that_is_not_present_is_not_shown()
    {
        Assert.Null(NetworkClassifier.Classify(NetworkInterfaceType.Ethernet, OperationalStatus.NotPresent, "Ethernet 3", "USB Ethernet/RNDIS Gadget"));
    }

    [Fact]
    public void Connected_first_then_wired_wifi_and_vpn()
    {
        var sorted = NetworkInterfaceStatus.Sorted(
        [
            Status("vpn", NetworkKind.Vpn, connected: true, "contoso-vpn"),
            Status("wifi", NetworkKind.WiFi, connected: true, "Home"),
            Status("down", NetworkKind.Wired, connected: false, null),
            Status("dock", NetworkKind.Wired, connected: true, "contoso.local")
        ]);

        Assert.Equal(["dock", "wifi", "vpn", "down"], sorted.Select(item => item.Id));
    }

    [Fact]
    public void Display_name_is_the_configured_one_else_the_network_else_the_interface()
    {
        var status = Status("id", NetworkKind.Wired, connected: true, "contoso.local");

        Assert.Equal("Dock", status.DisplayName(new NetworkInterfaceSettings { Id = "id", Name = "Dock" }));
        Assert.Equal("contoso.local", status.DisplayName(null));
        Assert.Equal("Ethernet", (status with { NetworkName = null }).DisplayName(null));
    }

    [Fact]
    public void Primary_address_is_the_first_ipv4_without_prefix()
    {
        var status = Status("id", NetworkKind.Wired, connected: true, null) with
        {
            IPv4 = ["192.168.1.20/24"],
            IPv6 = ["2a02::1/64"]
        };

        Assert.Equal("192.168.1.20", status.PrimaryAddress);
        Assert.Equal("2a02::1", (status with { IPv4 = [] }).PrimaryAddress);
    }

    [Fact]
    public void Details_text_has_one_label_per_detail_and_indents_extra_lines()
    {
        var status = Status("id", NetworkKind.Wired, connected: true, "contoso.local") with
        {
            IPv4 = ["10.0.0.5/24"],
            DnsServers = ["10.0.0.1", "10.0.0.2"]
        };

        var lines = status.DetailsText(null).Split(Environment.NewLine);

        Assert.Contains("IPv4: 10.0.0.5/24", lines);
        Assert.Contains("DNS servers: 10.0.0.1", lines);
        Assert.Contains("             10.0.0.2", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("Gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_with_no_name_hidden_flag_or_connect_action_are_removed()
    {
        var config = new ConfigurationFile();
        var status = Status("{A}", NetworkKind.Vpn, connected: true, null);

        NetworkInterfaceSettings.Set(config, status, " Dock ", hidden: false, connect: null);
        var settings = NetworkInterfaceSettings.Find(config, "{a}");
        Assert.Equal("Dock", settings?.Name);
        Assert.Equal(NetworkKind.Vpn, settings?.Kind);
        Assert.Equal("Ethernet", settings?.Interface);

        NetworkInterfaceSettings.Set(config, status, "", hidden: false, connect: "Proton VPN");
        Assert.Null(NetworkInterfaceSettings.Find(config, "{A}")?.Name);
        Assert.Equal("Proton VPN", NetworkInterfaceSettings.Find(config, "{A}")?.Connect);

        NetworkInterfaceSettings.Set(config, status, null, hidden: false, connect: " ");
        Assert.Empty(config.NetworkInterfaces);
    }

    [Fact]
    public void Interfaces_with_settings_stay_listed_while_absent()
    {
        var present = new[] { Status("{A}", NetworkKind.Wired, connected: true, "contoso.local") };
        var settings = new[]
        {
            new NetworkInterfaceSettings { Id = "{a}", Name = "Dock" },
            new NetworkInterfaceSettings { Id = "{B}", Kind = NetworkKind.Vpn, Interface = "ProTUN", Connect = "Proton VPN" }
        };

        var all = NetworkInterfaceSettings.WithAbsent(present, settings);

        Assert.Equal(["{A}", "{B}"], all.Select(item => item.Id));
        Assert.False(all[1].Connected);
        Assert.Equal(NetworkKind.Vpn, all[1].Kind);
        Assert.Equal("ProTUN", all[1].InterfaceName);
    }

    [Fact]
    public void Renaming_an_action_follows_the_link_and_removing_it_drops_the_link()
    {
        var config = new ConfigurationFile
        {
            NetworkInterfaces =
            [
                new NetworkInterfaceSettings { Id = "{A}", Connect = "Proton" },
                new NetworkInterfaceSettings { Id = "{B}", Name = "Office", Connect = "Proton" }
            ]
        };

        ActivityOperations.RenameAction(config, "proton", "Proton VPN");
        Assert.All(config.NetworkInterfaces, network => Assert.Equal("Proton VPN", network.Connect));

        ActivityOperations.RemoveAction(config, "Proton VPN");
        var left = Assert.Single(config.NetworkInterfaces);
        Assert.Equal("{B}", left.Id);
        Assert.Null(left.Connect);
    }

    [Fact]
    public void Phonebook_vpn_entries_are_the_sections_of_type_2()
    {
        const string phonebook = "[mail.example.com]\r\nEncoding=1\r\nType=2\r\nPhoneNumber=mail.example.com\r\n\r\n[Dial-up]\r\nType=1\r\n\r\n[contoso-vpn]\r\nType=2\r\n";

        Assert.Equal(["mail.example.com", "contoso-vpn"], NetworkInterfaces.ParseVpnEntries(phonebook));
    }

    [Theory]
    [InlineData(NetworkKind.WiFi, null, "ms-availablenetworks:", null)]
    [InlineData(NetworkKind.Wired, null, "ms-settings:network-ethernet", null)]
    [InlineData(NetworkKind.Vpn, null, "ms-settings:network-vpn", null)]
    [InlineData(NetworkKind.Vpn, "contoso-vpn", "rasphone.exe", "-d \"contoso-vpn\"")]
    public void Connect_opens_the_dialog_for_the_kind(NetworkKind kind, string? entry, string fileName, string? arguments)
    {
        var status = Status("id", kind, connected: false, null) with { VpnEntry = entry };

        Assert.Equal((fileName, arguments), status.ConnectCommand);
    }

    private static NetworkInterfaceStatus Status(string id, NetworkKind kind, bool connected, string? network) => new()
    {
        Id = id,
        Kind = kind,
        InterfaceName = "Ethernet",
        Description = "Adapter",
        Connected = connected,
        NetworkName = network
    };
}
