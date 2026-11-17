using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DevWorkspaceLauncher;

/// <summary>What kind of connection a network interface is.</summary>
public enum NetworkKind
{
    Wired,
    WiFi,
    Mobile,
    Vpn
}

public sealed class NetworkKindConverter() : CaseInsensitiveEnumConverter<NetworkKind>("Kind");

/// <summary>The launcher's settings for one network interface, by its adapter id.</summary>
public sealed class NetworkInterfaceSettings
{
    /// <summary>
    /// The adapter's id, a GUID such as <c>{0F1E...}</c>; it stays the same when the adapter is renamed.
    /// A Windows VPN connection's is <c>vpn:</c> and its name.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>The name shown instead of the network's or the adapter's.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Not shown on the start page unless hidden interfaces are shown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hidden { get; set; }

    /// <summary>
    /// The action Connect runs - the VPN client's app, say - instead of opening Windows' dialog. Matched
    /// by name, case-insensitively.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Connect { get; set; }

    /// <summary>
    /// The interface's kind when last seen, so it is still shown, with the right icon, while its adapter
    /// is gone: a VPN client's adapter only exists while connected, a dock's only while docked.
    /// </summary>
    [JsonConverter(typeof(NetworkKindConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkKind? Kind { get; set; }

    /// <summary>The interface's name in Windows when last seen; see <see cref="Kind"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Interface { get; set; }

    /// <summary>The settings for <paramref name="id"/> in <paramref name="config"/>, null when it has none.</summary>
    public static NetworkInterfaceSettings? Find(ConfigurationFile config, string id) =>
        config.NetworkInterfaces.FirstOrDefault(settings => string.Equals(settings.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sets the name, hidden flag and connect action for <paramref name="status"/>, and remembers its kind
    /// and interface name; an entry left with none of the three is removed. Blank names clear them.
    /// </summary>
    public static void Set(ConfigurationFile config, NetworkInterfaceStatus status, string? name, bool hidden, string? connect)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        connect = string.IsNullOrWhiteSpace(connect) ? null : connect.Trim();
        var settings = Find(config, status.Id);
        if (name is null && !hidden && connect is null)
        {
            if (settings is not null)
            {
                config.NetworkInterfaces.Remove(settings);
            }

            return;
        }

        if (settings is null)
        {
            settings = new NetworkInterfaceSettings { Id = status.Id };
            config.NetworkInterfaces.Add(settings);
        }

        settings.Name = name;
        settings.Hidden = hidden;
        settings.Connect = connect;
        settings.Kind = status.Kind;
        settings.Interface = status.InterfaceName;
    }

    /// <summary>
    /// <paramref name="present"/> plus a disconnected entry for each interface that has settings but is
    /// not there now, shown under its remembered kind and name.
    /// </summary>
    public static IReadOnlyList<NetworkInterfaceStatus> WithAbsent(IReadOnlyList<NetworkInterfaceStatus> present, IEnumerable<NetworkInterfaceSettings> settings)
    {
        var absent = settings
            .Where(entry => !present.Any(status => string.Equals(status.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new NetworkInterfaceStatus
            {
                Id = entry.Id,
                Kind = entry.Kind ?? NetworkKind.Wired,
                InterfaceName = entry.Interface ?? entry.Name ?? entry.Id,
                Description = "Not present now",
                Connected = false
            });

        return NetworkInterfaceStatus.Sorted([.. present, .. absent]);
    }
}

/// <summary>What Windows' connection profile adds about an interface: the network's name and internet access.</summary>
public sealed record NetworkProfileInfo(string? NetworkName, bool Internet);

/// <summary>One network interface with its addresses, as the start page shows it.</summary>
public sealed record NetworkInterfaceStatus
{
    public required string Id { get; init; }
    public required NetworkKind Kind { get; init; }

    /// <summary>The adapter's name in Windows, such as <c>Ethernet 2</c>.</summary>
    public required string InterfaceName { get; init; }

    public required string Description { get; init; }
    public required bool Connected { get; init; }

    /// <summary>The network's name: the SSID for Wi-Fi, the domain or network for wired, the connection for a VPN.</summary>
    public string? NetworkName { get; init; }

    /// <summary>Whether it reaches the internet; null when Windows did not say.</summary>
    public bool? Internet { get; init; }

    /// <summary>IPv4 addresses as <c>address/prefix</c>.</summary>
    public IReadOnlyList<string> IPv4 { get; init; } = [];

    /// <summary>IPv6 addresses as <c>address/prefix</c>, global ones before link-local ones.</summary>
    public IReadOnlyList<string> IPv6 { get; init; } = [];

    public IReadOnlyList<string> Gateways { get; init; } = [];
    public IReadOnlyList<string> DnsServers { get; init; } = [];
    public string? DnsSuffix { get; init; }
    public bool DhcpEnabled { get; init; }
    public IReadOnlyList<string> DhcpServers { get; init; } = [];

    /// <summary>The MAC address as <c>AA-BB-CC-DD-EE-FF</c>; empty when the adapter has none.</summary>
    public string Mac { get; init; } = string.Empty;

    /// <summary>The link speed in bits per second; 0 or less when unknown.</summary>
    public long Speed { get; init; }

    /// <summary>The Windows VPN connection (phonebook entry) this is, for a built-in VPN; null otherwise.</summary>
    public string? VpnEntry { get; init; }

    /// <summary>
    /// What Connect opens: the VPN's own connect dialog for a built-in VPN, the available Wi-Fi networks
    /// for Wi-Fi, else the Settings page for the kind of connection.
    /// </summary>
    public (string FileName, string? Arguments) ConnectCommand => VpnEntry is { } entry
        ? ("rasphone.exe", $"-d \"{entry}\"")
        : (Kind switch
        {
            NetworkKind.WiFi => "ms-availablenetworks:",
            NetworkKind.Mobile => "ms-settings:network-cellular",
            NetworkKind.Vpn => "ms-settings:network-vpn",
            _ => "ms-settings:network-ethernet"
        }, null);

    public string KindText => Kind switch
    {
        NetworkKind.WiFi => "Wi-Fi",
        NetworkKind.Mobile => "Mobile",
        NetworkKind.Vpn => "VPN",
        _ => "Wired"
    };

    /// <summary>The name to show: the configured one, else the network's, else the adapter's.</summary>
    public string DisplayName(NetworkInterfaceSettings? settings) =>
        settings?.Name ?? (string.IsNullOrWhiteSpace(NetworkName) ? InterfaceName : NetworkName);

    /// <summary>The address to show on the chip: the first IPv4 address, else the first IPv6 one, without the prefix.</summary>
    public string? PrimaryAddress => (IPv4.FirstOrDefault() ?? IPv6.FirstOrDefault())?.Split('/')[0];

    /// <summary>Every detail as label and value, in the order the details popup shows them; empty values left out.</summary>
    public IReadOnlyList<(string Label, string Value)> Details(NetworkInterfaceSettings? settings)
    {
        var details = new List<(string Label, string Value)>
        {
            ("Name", DisplayName(settings)),
            ("Kind", KindText),
            ("Status", Connected ? (Internet == false ? "Connected, no internet" : "Connected") : "Disconnected"),
            ("Network", NetworkName ?? string.Empty),
            ("Interface", InterfaceName),
            ("Adapter", Description),
            ("IPv4", string.Join(Environment.NewLine, IPv4)),
            ("IPv6", string.Join(Environment.NewLine, IPv6)),
            ("Gateway", string.Join(Environment.NewLine, Gateways)),
            ("DNS servers", string.Join(Environment.NewLine, DnsServers)),
            ("DNS suffix", DnsSuffix ?? string.Empty),
            ("DHCP", Connected && IPv4.Count > 0 ? (DhcpEnabled ? $"Yes {string.Join(", ", DhcpServers)}".Trim() : "No (static)") : string.Empty),
            ("MAC", Mac),
            ("Speed", Connected ? SpeedText(Speed) : string.Empty),
            ("Connect with", settings?.Connect ?? string.Empty),
            ("Id", Id)
        };

        return details.Where(detail => !string.IsNullOrWhiteSpace(detail.Value)).ToList();
    }

    /// <summary>The details as text, one <c>Label: value</c> per line, for the clipboard.</summary>
    public string DetailsText(NetworkInterfaceSettings? settings)
    {
        var text = new StringBuilder();
        foreach (var (label, value) in Details(settings))
        {
            var lines = value.Split(Environment.NewLine);
            text.Append(label).Append(": ").AppendLine(lines[0]);
            foreach (var line in lines.Skip(1))
            {
                text.Append(' ', label.Length + 2).AppendLine(line);
            }
        }

        return text.ToString();
    }

    public static string SpeedText(long bitsPerSecond) => bitsPerSecond switch
    {
        <= 0 => string.Empty,
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gbit/s",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:0.#} Mbit/s",
        _ => $"{bitsPerSecond / 1000.0:0.#} kbit/s"
    };

    /// <summary>The order interfaces are shown in: connected first, then wired, Wi-Fi, mobile, VPN, each by name.</summary>
    public static IReadOnlyList<NetworkInterfaceStatus> Sorted(IEnumerable<NetworkInterfaceStatus> interfaces) =>
        interfaces.OrderByDescending(item => item.Connected)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.NetworkName ?? item.InterfaceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

public static partial class NetworkClassifier
{
    /// <summary>Adapter descriptions of VPN clients that show up as an ordinary Ethernet adapter.</summary>
    private static readonly string[] VpnAdapters =
    [
        "VPN", "TAP-Windows", "TAP Adapter", "WireGuard", "Wintun", "OpenVPN", "Fortinet", "AnyConnect",
        "PANGP", "GlobalProtect", "Juniper", "Pulse Secure", "Tailscale", "ZeroTier", "NordLynx"
    ];

    /// <summary>
    /// Adapters that are not a network connection: local virtual switches (they connect to this PC's own
    /// VMs), the plumbing Windows lists as adapters, and pseudo-interfaces for IPv6 transition.
    /// </summary>
    private static readonly string[] NotNetworks =
    [
        "Hyper-V Virtual Ethernet", "Hyper-V Virtual Switch Extension", "WSL", "VirtualBox Host-Only",
        "VMware Virtual Ethernet", "WAN Miniport", "Wi-Fi Direct Virtual", "Kernel Debug", "Teredo",
        "6to4", "IP-HTTPS", "ISATAP"
    ];

    /// <summary>
    /// The kind of an interface, or null when it is not shown: not present, loopback, a filter driver's
    /// pseudo-interface, a local virtual switch or other plumbing.
    /// </summary>
    public static NetworkKind? Classify(NetworkInterfaceType type, OperationalStatus status, string name, string description)
    {
        if (status == OperationalStatus.NotPresent
            || type == NetworkInterfaceType.Loopback
            || FilterInterface().IsMatch(name)
            || NotNetworks.Any(text => description.Contains(text, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (type == NetworkInterfaceType.Wireless80211)
        {
            return NetworkKind.WiFi;
        }

        if (type is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2)
        {
            return NetworkKind.Mobile;
        }

        // 53 is IANA's propVirtual, which .NET has no name for; VPN clients such as OpenVPN Connect use it.
        if (type is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel or (NetworkInterfaceType)53
            || VpnAdapters.Any(text => description.Contains(text, StringComparison.OrdinalIgnoreCase)))
        {
            return NetworkKind.Vpn;
        }

        return NetworkKind.Wired;
    }

    /// <summary>Filter drivers bound to an adapter show up as <c>Adapter-Filter Name-0000</c>.</summary>
    [GeneratedRegex(@"-\d{4}$")]
    private static partial Regex FilterInterface();
}

public static class NetworkInterfaces
{
    /// <summary>The VPN connections in the user's and the machine's phonebooks, each once.</summary>
    public static IReadOnlyList<string> VpnEntries()
    {
        string[] phonebooks =
        [
            Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Network\Connections\Pbk\rasphone.pbk"),
            Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Network\Connections\Pbk\rasphone.pbk")
        ];

        var entries = new List<string>();
        foreach (var phonebook in phonebooks)
        {
            try
            {
                if (File.Exists(phonebook))
                {
                    entries.AddRange(ParseVpnEntries(File.ReadAllText(phonebook)));
                }
            }
            catch (IOException)
            {
                // Being written; the next refresh reads it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return entries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The names of the VPN entries (<c>Type=2</c>) in a <c>rasphone.pbk</c> phonebook.</summary>
    public static IEnumerable<string> ParseVpnEntries(string phonebook)
    {
        string? section = null;
        foreach (var raw in phonebook.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
            }
            else if (section is not null && line.Replace(" ", string.Empty) == "Type=2")
            {
                yield return section;
                section = null;
            }
        }
    }

    /// <summary>
    /// The interfaces worth showing, connected or not, with their addresses. <paramref name="profile"/>
    /// gives Windows' connection profile for an adapter id, null when it has none.
    /// </summary>
    public static IReadOnlyList<NetworkInterfaceStatus> Read(Func<string, NetworkProfileInfo?> profile)
    {
        var found = new List<NetworkInterfaceStatus>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var kind = NetworkClassifier.Classify(adapter.NetworkInterfaceType, adapter.OperationalStatus, adapter.Name, adapter.Description);
                if (kind is null)
                {
                    continue;
                }

                var connected = adapter.OperationalStatus == OperationalStatus.Up;
                var properties = adapter.GetIPProperties();
                var info = connected ? profile(adapter.Id) : null;
                // A disconnected adapter keeps only a self-assigned 169.254 address, of no use to anyone.
                var unicast = connected ? properties.UnicastAddresses.ToList() : [];
                found.Add(new NetworkInterfaceStatus
                {
                    Id = adapter.Id,
                    Kind = kind.Value,
                    InterfaceName = adapter.Name,
                    Description = adapter.Description,
                    Connected = connected,
                    NetworkName = info?.NetworkName,
                    Internet = info?.Internet,
                    IPv4 = unicast.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(address => $"{address.Address}/{address.PrefixLength}").ToList(),
                    IPv6 = unicast.Where(address => address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        .OrderBy(address => address.Address.IsIPv6LinkLocal)
                        .Select(address => $"{address.Address}/{address.PrefixLength}").ToList(),
                    Gateways = properties.GatewayAddresses.Select(gateway => gateway.Address.ToString())
                        .Where(gateway => gateway is not "0.0.0.0" and not "::").ToList(),
                    DnsServers = properties.DnsAddresses.Where(dns => !dns.IsIPv6SiteLocal).Select(dns => dns.ToString()).ToList(),
                    DnsSuffix = string.IsNullOrWhiteSpace(properties.DnsSuffix) ? null : properties.DnsSuffix,
                    DhcpEnabled = adapter.Supports(NetworkInterfaceComponent.IPv4) && properties.GetIPv4Properties()?.IsDhcpEnabled == true,
                    DhcpServers = properties.DhcpServerAddresses.Select(server => server.ToString()).ToList(),
                    Mac = string.Join("-", adapter.GetPhysicalAddress().GetAddressBytes().Select(part => part.ToString("X2"))),
                    Speed = connected ? adapter.Speed : 0
                });
            }
            catch (NetworkInformationException)
            {
                // An adapter that goes away while it is read; the next change lists the rest again.
            }
        }

        // Windows' own VPN connections only have an adapter while connected: list the others from the
        // phonebooks, and mark the connected ones as the entry they are.
        foreach (var entry in VpnEntries())
        {
            var index = found.FindIndex(item => item.Kind == NetworkKind.Vpn && string.Equals(item.InterfaceName, entry, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                found[index] = found[index] with { VpnEntry = entry };
            }
            else
            {
                found.Add(new NetworkInterfaceStatus
                {
                    Id = $"vpn:{entry}",
                    Kind = NetworkKind.Vpn,
                    InterfaceName = entry,
                    Description = "Windows VPN connection",
                    Connected = false,
                    VpnEntry = entry
                });
            }
        }

        return NetworkInterfaceStatus.Sorted(found);
    }
}
