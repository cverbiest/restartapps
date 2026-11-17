using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Networking.Connectivity;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>A network interface as the start page shows it.</summary>
public sealed class NetworkItem(NetworkInterfaceStatus status, NetworkInterfaceSettings? settings)
{
    public NetworkInterfaceStatus Status => status;

    public NetworkInterfaceSettings? Settings => settings;

    public string Name => status.DisplayName(settings);

    /// <summary>The IP address when connected, else "disconnected".</summary>
    public string Address => status.Connected ? status.PrimaryAddress ?? "no address" : "disconnected";

    /// <summary>Shown after the address when the interface is connected but does not reach the internet.</summary>
    public string Note => status.Connected && status.Internet == false ? "no internet" : string.Empty;

    public string Glyph => status.Kind switch
    {
        NetworkKind.WiFi => "",
        NetworkKind.Mobile => "",
        NetworkKind.Vpn => "",
        _ => ""
    };

    /// <summary>Green when connected, grey when not.</summary>
    public Brush IconBrush => (Brush)Application.Current.Resources[status.Connected ? "SystemFillColorSuccessBrush" : "TextFillColorDisabledBrush"];

    /// <summary>Hidden interfaces, when shown, are dimmed.</summary>
    public double Opacity => settings?.Hidden == true ? 0.55 : 1.0;

    public string Tooltip => $"{status.KindText}: {status.InterfaceName} ({status.Description}){Environment.NewLine}Click for details, right-click to connect or copy";

    public string ConnectText => ConnectLabel(status, settings?.Connect);

    /// <summary>The Connect button's text: the linked action, or what Windows' dialog is for.</summary>
    public static string ConnectLabel(NetworkInterfaceStatus status, string? connectWith) => connectWith is not null
        ? status.Connected ? $"Open {connectWith}" : $"Connect with {connectWith}"
        : status.Connected ? "Network settings" : "Connect...";

    /// <summary>What the chip shows, to tell whether a refresh changed anything.</summary>
    public string Signature => $"{status.DetailsText(settings)}|{Name}|{Opacity}";
}

/// <summary>The network interfaces of this PC, and when they change.</summary>
internal static class NetworkStatus
{
    private const int IanaPpp = 23;
    private const int IanaPropVirtual = 53;
    private const int IanaTunnel = 131;

    /// <summary>
    /// Raised, on a background thread, when Windows reports a change: a network connected or dropped,
    /// its internet access changed, or an address changed. Several may come for one change.
    /// </summary>
    public static event EventHandler? Changed
    {
        add
        {
            if (_changed is null)
            {
                NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnAddressChanged;
            }

            _changed += value;
        }
        remove
        {
            _changed -= value;
            if (_changed is null)
            {
                NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            }
        }
    }

    private static EventHandler? _changed;

    private static void OnNetworkStatusChanged(object sender) => _changed?.Invoke(null, EventArgs.Empty);

    private static void OnAddressChanged(object? sender, EventArgs e) => _changed?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// The interfaces worth showing, connected first (see <see cref="NetworkInterfaces.Read"/>), plus those
    /// with <paramref name="settings"/> that are not there now.
    /// </summary>
    public static IReadOnlyList<NetworkInterfaceStatus> Current(IReadOnlyList<NetworkInterfaceSettings> settings)
    {
        var profiles = Profiles();
        return NetworkInterfaceSettings.WithAbsent(NetworkInterfaces.Read(id => profiles.GetValueOrDefault(id)), settings);
    }

    /// <summary>
    /// Connects <paramref name="status"/>: runs the action it is linked to (<paramref name="connectWith"/>),
    /// or opens Windows' dialog for it when it has none or that action no longer exists.
    /// </summary>
    public static void Connect(AppState state, NetworkInterfaceStatus status, string? connectWith)
    {
        if (connectWith is not null)
        {
            var action = state.Config.Actions.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, connectWith, StringComparison.OrdinalIgnoreCase));
            if (action is not null)
            {
                state.RunAction(action);
                return;
            }

            state.LogMessage($"No action '{connectWith}' to connect {status.InterfaceName} with; opening Windows' dialog instead.");
        }

        var (fileName, arguments) = status.ConnectCommand;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, arguments ?? string.Empty) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            state.LogMessage($"Could not open {fileName}: {ex.Message}");
        }
    }

    /// <summary>The connected profile of each adapter, by adapter id (<c>{GUID}</c>).</summary>
    private static Dictionary<string, NetworkProfileInfo> Profiles()
    {
        var profiles = new Dictionary<string, NetworkProfileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in NetworkInformation.GetConnectionProfiles())
        {
            try
            {
                var level = profile.GetNetworkConnectivityLevel();
                if (level == NetworkConnectivityLevel.None || profile.NetworkAdapter is not { } adapter)
                {
                    continue;
                }

                // Wi-Fi: the SSID. A VPN: the connection's name. Wired: the network's name (such as the
                // domain), as ProfileName is only the adapter's name there.
                var name = profile.IsWlanConnectionProfile
                    ? profile.WlanConnectionProfileDetails?.GetConnectedSsid()
                    : adapter.IanaInterfaceType is IanaPpp or IanaPropVirtual or IanaTunnel
                        ? profile.ProfileName
                        : profile.GetNetworkNames().FirstOrDefault();
                profiles[adapter.NetworkAdapterId.ToString("B")] = new NetworkProfileInfo(
                    string.IsNullOrWhiteSpace(name) ? profile.ProfileName : name,
                    level == NetworkConnectivityLevel.InternetAccess);
            }
            catch (Exception)
            {
                // A profile that goes away while it is read; the next change event lists the rest again.
            }
        }

        return profiles;
    }
}
