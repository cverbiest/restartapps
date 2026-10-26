using System.Globalization;
using System.Text;

namespace DevWorkspaceLauncher;

/// <summary>Exit codes of an RDP launch, the same numbers Remote Desktop Plus uses.</summary>
public static class RdpExitCode
{
    public const int Ok = 0;
    public const int GeneralError = 1;
    public const int ConnectionFileNotFound = 2;
    public const int ProfileNotFound = 30;
    public const int InvalidParameters = 87;
}

/// <summary>A launch that cannot go ahead; the message is a complete line for the log.</summary>
public sealed class RdpLaunchException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>The built-in size presets, and looking one up by name.</summary>
public static class RdpPresets
{
    /// <summary>68% of the working area, zoomed to 150%.</summary>
    public static RdpSizePreset Fit68 => new() { Name = "fit68", Mode = RdpDisplayMode.Fit, Fit = 68, Zoom = 150 };

    public static RdpSizePreset Full => new() { Name = "full", Mode = RdpDisplayMode.FullScreen };

    public static IReadOnlyList<RdpSizePreset> BuiltIn => [Fit68, Full];

    /// <summary>The presets on offer: those in the file, then the built-in ones the file does not replace.</summary>
    public static IReadOnlyList<RdpSizePreset> All(ConfigurationFile config) =>
        [.. config.RdpSizes, .. BuiltIn.Where(builtIn => Find(config.RdpSizes, builtIn.Name) is null)];

    public static RdpSizePreset? Find(ConfigurationFile config, string name) =>
        Find(config.RdpSizes, name) ?? Find(BuiltIn, name);

    private static RdpSizePreset? Find(IEnumerable<RdpSizePreset> presets, string name) =>
        presets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The <c>rdp</c> command line: <c>[file.rdp] /switch[:value] ...</c>. Parses into the same
/// <see cref="RdpSettings"/> a saved session uses, so the two merge.
/// </summary>
public sealed class RdpCommandLine
{
    /// <summary>The <c>/fit</c> percentage when no number is given.</summary>
    public const int DefaultFit = 90;

    private Action<string>? _unknown;

    public string? SessionName { get; private set; }

    /// <summary>What the switches set; absent switches stay null.</summary>
    public RdpSettings Settings { get; } = new();

    /// <summary>Wait for mstsc to exit before returning.</summary>
    public bool Wait { get; private set; }

    /// <summary>Report problems only through the exit code, never in a dialog.</summary>
    public bool Batch { get; private set; }

    /// <summary>
    /// Parses the arguments. An unknown switch is invalid, unless <paramref name="unknown"/> is given:
    /// then it is handed there and skipped, which is how an import reports what it could not take over.
    /// </summary>
    public static RdpCommandLine Parse(IEnumerable<string> args, Action<string>? unknown = null)
    {
        var result = new RdpCommandLine { _unknown = unknown };
        var first = true;

        foreach (var arg in args)
        {
            if (first && !arg.StartsWith('/') && arg.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase))
            {
                result.Settings.RdpFile = arg;
                first = false;
                continue;
            }

            first = false;
            if (!arg.StartsWith('/') || arg.Length == 1)
            {
                throw Invalid($"Unexpected argument '{arg}'. Switches start with '/'.");
            }

            var colon = arg.IndexOf(':');
            var name = (colon < 0 ? arg[1..] : arg[1..colon]).ToLowerInvariant();
            var value = colon < 0 ? null : arg[(colon + 1)..];
            result.Apply(name, value);
        }

        return result;
    }

    private void Apply(string name, string? value)
    {
        var settings = Settings;
        switch (name)
        {
            case "session":
                SessionName = Required(name, value);
                break;
            case "v":
                settings.Host = Required(name, value);
                break;
            case "cred":
            case "i":
                // /i is RDP+'s credential profile; here it names the Credential Manager entry.
                settings.Credential = Required(name, value);
                break;
            case "u":
                settings.Username = Required(name, value);
                break;
            case "domain":
                settings.Domain = Required(name, value);
                break;
            case "size":
                settings.Size = Required(name, value);
                break;
            case "f":
            case "fullscreen":
                settings.Mode = RdpDisplayMode.FullScreen;
                break;
            case "max":
                settings.Mode = RdpDisplayMode.Max;
                break;
            case "fit":
                settings.Mode = RdpDisplayMode.Fit;
                settings.Fit = value is null ? DefaultFit : Number(name, value.TrimEnd('%'));
                break;
            case "w":
                settings.Mode = RdpDisplayMode.Size;
                settings.Width = Number(name, Required(name, value));
                break;
            case "h":
                settings.Mode = RdpDisplayMode.Size;
                settings.Height = Number(name, Required(name, value));
                break;
            case "mon":
                settings.Monitor = Number(name, Required(name, value));
                break;
            case "zoom":
                settings.Zoom = Number(name, Required(name, value).TrimEnd('%'));
                break;
            case "admin":
            case "console":
                settings.Admin = true;
                break;
            case "o":
                settings.Options = [.. settings.Options ?? [], .. SplitOptions(Required(name, value))];
                break;
            case "wait":
                Wait = true;
                break;
            case "batch":
                Batch = true;
                break;
            case "title":
                settings.Title = Required(name, value);
                break;
            case "drives":
                settings.Drives = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim().Trim('"');
                break;
            default:
                if (_unknown is null)
                {
                    throw Invalid($"Unknown switch '/{name}'.");
                }

                _unknown(value is null ? $"/{name}" : $"/{name}:{value}");
                break;
        }
    }

    /// <summary>
    /// Splits one command-line string into arguments on spaces, keeping quoted parts together - so
    /// <c>/title:"%s (%u)"</c> stays one argument, quotes included.
    /// </summary>
    public static List<string> Tokenize(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    /// <summary>Splits <c>key:type:value,...</c> on commas, where <c>\,</c> is a literal comma.</summary>
    internal static List<string> SplitOptions(string value)
    {
        var items = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] == ',')
            {
                current.Append(',');
                i++;
            }
            else if (value[i] == ',')
            {
                items.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(value[i]);
            }
        }

        items.Add(current.ToString());
        return items.Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
    }

    private static string Required(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw Invalid($"/{name} needs a value: /{name}:value.") : value.Trim().Trim('"');

    private static int Number(string name, string value) =>
        int.TryParse(value.Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw Invalid($"/{name} needs a positive whole number, not '{value}'.");

    private static RdpLaunchException Invalid(string message) => new(RdpExitCode.InvalidParameters, message);
}

/// <summary>Merges the option layers into the settings one launch uses.</summary>
public static class RdpResolver
{
    /// <summary>
    /// Layers, last wins: the size preset, the saved session, the command line. A <c>/size</c> on the
    /// command line replaces the session's whole display, so the preset asked for is what you get.
    /// </summary>
    public static RdpSettings Resolve(ConfigurationFile config, RdpCommandLine commandLine) =>
        Resolve(config, FindSession(config, commandLine.SessionName), commandLine.Settings);

    public static RdpSettings Resolve(ConfigurationFile config, RdpSession? session, RdpSettings? overrides = null)
    {
        overrides ??= new RdpSettings();
        var resolved = new RdpSettings();

        var sizeName = overrides.Size ?? session?.Size;
        if (sizeName is not null)
        {
            var preset = RdpPresets.Find(config, sizeName)
                ?? throw new RdpLaunchException(RdpExitCode.InvalidParameters, $"Unknown size preset '{sizeName}'.");
            resolved.OverlayDisplay(preset);
        }

        if (session is not null)
        {
            resolved.Overlay(session);
            if (overrides.Size is not null)
            {
                // The command line chose a preset: the session's own display settings give way to it.
                resolved.Mode = null;
                resolved.Fit = resolved.Width = resolved.Height = resolved.Monitor = resolved.Zoom = null;
                resolved.OverlayDisplay(RdpPresets.Find(config, overrides.Size)!);
            }
        }

        resolved.Overlay(overrides);
        resolved.Size = sizeName;

        if (string.IsNullOrWhiteSpace(resolved.Host))
        {
            throw new RdpLaunchException(RdpExitCode.InvalidParameters, "No computer to connect to: give /v:host or /session:name.");
        }

        if (resolved.Mode == RdpDisplayMode.Size && (resolved.Width is null || resolved.Height is null))
        {
            throw new RdpLaunchException(RdpExitCode.InvalidParameters, "A fixed size needs both a width (/w) and a height (/h).");
        }

        return resolved;
    }

    public static RdpSession? FindSession(ConfigurationFile config, string? name)
    {
        if (name is null)
        {
            return null;
        }

        return config.RdpSessions.FirstOrDefault(session => string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new RdpLaunchException(RdpExitCode.ProfileNotFound, $"No RDP session named '{name}'.");
    }

    /// <summary>Splits <c>host:port</c>; a bare host gets the default port 3389.</summary>
    public static (string Host, int Port) SplitHost(string host)
    {
        host = host.Trim();
        var colon = host.LastIndexOf(':');
        if (colon > 0 && !host.EndsWith(']') && host.IndexOf(':') == colon
            && int.TryParse(host[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and < 65536)
        {
            return (host[..colon], port);
        }

        return (host, 3389);
    }
}
