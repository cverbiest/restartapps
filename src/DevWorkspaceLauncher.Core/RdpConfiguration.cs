using System.Text.Json.Serialization;

namespace DevWorkspaceLauncher;

/// <summary>How an RDP session window is sized.</summary>
public enum RdpDisplayMode
{
    /// <summary>Full screen on the target monitor.</summary>
    FullScreen,

    /// <summary>A window filling the monitor's working area, so it looks like a maximised app.</summary>
    Max,

    /// <summary>
    /// A window scaled to the working area: <see cref="RdpDisplay.Fit"/> up to 100 is a percentage of
    /// it, above 100 a margin in pixels on each side.
    /// </summary>
    Fit,

    /// <summary>A window whose client area is exactly <see cref="RdpDisplay.Width"/> by <see cref="RdpDisplay.Height"/>.</summary>
    Size
}

public sealed class RdpDisplayModeConverter() : CaseInsensitiveEnumConverter<RdpDisplayMode>("Mode");

/// <summary>
/// Window settings shared by size presets, sessions and the command line. Every property is optional:
/// a layer only overrides what it sets.
/// </summary>
public class RdpDisplay
{
    [JsonConverter(typeof(RdpDisplayModeConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RdpDisplayMode? Mode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Fit { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; set; }

    /// <summary>The monitor to start on, 1-based. Absent means the primary monitor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Monitor { get; set; }

    /// <summary>Remote DPI scaling in percent (<c>desktopscalefactor</c>), what mstsc's Zoom menu sets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Zoom { get; set; }

    [JsonIgnore]
    public bool HasDisplay => Mode is not null || Fit is not null || Width is not null || Height is not null
        || Monitor is not null || Zoom is not null;

    /// <summary>Copies every display setting <paramref name="other"/> sets over this one.</summary>
    public void OverlayDisplay(RdpDisplay other)
    {
        Mode = other.Mode ?? Mode;
        Fit = other.Fit ?? Fit;
        Width = other.Width ?? Width;
        Height = other.Height ?? Height;
        Monitor = other.Monitor ?? Monitor;
        Zoom = other.Zoom ?? Zoom;
    }
}

/// <summary>A named, reusable window size, referenced by <c>/size:name</c> and by sessions.</summary>
public sealed class RdpSizePreset : RdpDisplay
{
    public required string Name { get; set; }
}

/// <summary>Connection settings shared by saved sessions and the command line.</summary>
public class RdpSettings : RdpDisplay
{
    /// <summary><c>host</c> or <c>host:port</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Host { get; set; }

    /// <summary>
    /// The target name of a generic credential in Windows Credential Manager. The password is read from
    /// there at launch; it is never stored in the configuration file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Credential { get; set; }

    /// <summary>Overrides the credential's username. May be <c>domain\user</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Domain { get; set; }

    /// <summary>The name of an <see cref="RdpSizePreset"/>; its settings sit under this layer's own.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Size { get; set; }

    /// <summary>Connect to the console / admin session (mstsc <c>/admin</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Admin { get; set; }

    /// <summary>An existing <c>.rdp</c> file whose settings the generated file starts from.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RdpFile { get; set; }

    /// <summary>
    /// Raw <c>.rdp</c> settings as <c>key:type:value</c>, for anything without a property of its own.
    /// Settings the launcher writes itself win over these.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Options { get; set; }

    /// <summary>
    /// The session window's title. <c>%s</c> is the computer, <c>%u</c> the user; other
    /// <c>%VARIABLES%</c> are expanded locally.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>
    /// Local drives to redirect: <c>*</c> for all (including ones plugged in later), or a list such as
    /// <c>c:,fixed,-d:,dynamic</c> of letters and the types fixed, removable, network, cdrom and
    /// dynamic, a <c>-</c> excluding.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Drives { get; set; }

    /// <summary>A short description of the window size, for lists: the preset, else the mode.</summary>
    [JsonIgnore]
    public string SizeText => Size ?? (Mode switch
    {
        RdpDisplayMode.Fit => $"fit {Fit ?? RdpCommandLine.DefaultFit}%",
        RdpDisplayMode.Size => $"{Width}x{Height}",
        RdpDisplayMode.Max => "max",
        _ => "full screen"
    });

    /// <summary>Copies everything <paramref name="other"/> sets over this one; raw options add up.</summary>
    public void Overlay(RdpSettings other)
    {
        OverlayDisplay(other);
        Title = other.Title ?? Title;
        Drives = other.Drives ?? Drives;
        Host = other.Host ?? Host;
        Credential = other.Credential ?? Credential;
        Username = other.Username ?? Username;
        Domain = other.Domain ?? Domain;
        Size = other.Size ?? Size;
        Admin = other.Admin ?? Admin;
        RdpFile = other.RdpFile ?? RdpFile;
        if (other.Options is { Count: > 0 })
        {
            Options = [.. Options ?? [], .. other.Options];
        }
    }
}

/// <summary>A saved RDP connection, shown on the RDP sessions page.</summary>
public sealed class RdpSession : RdpSettings
{
    public required string Name { get; set; }

    /// <summary>Shown on the start page and in the tray menu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Favorite { get; set; }
}
