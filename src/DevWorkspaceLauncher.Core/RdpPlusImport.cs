using System.Text.RegularExpressions;

namespace DevWorkspaceLauncher;

/// <summary>One Remote Desktop Plus favorite, turned into session settings.</summary>
/// <param name="Unsupported">The switches it had that the launcher does not support (yet); left out.</param>
public sealed record RdpImportEntry(string Name, RdpSettings Settings, IReadOnlyList<string> Unsupported);

/// <summary>What an import added and what it left out.</summary>
public sealed record RdpImportResult(IReadOnlyList<RdpSession> Added, IReadOnlyList<string> Skipped);

/// <summary>
/// Imports Remote Desktop Plus favorites: sessions and their options only. RDP+ credential profiles
/// are not imported; a favorite's <c>/i:profile</c> becomes the session's Credential, so one generic
/// credential of that name in Credential Manager serves every session that used the profile.
/// <para>
/// RDP+ stores favorites encrypted, so the input is the decrypted listing that
/// <c>tools/Export-RdpPlusFavorites.ps1</c> writes: one <c>name | secure=... | command line</c> per line.
/// </para>
/// </summary>
public static partial class RdpPlusImport
{
    [GeneratedRegex(@"^\s*(?<name>.+?)\s*\|\s*secure=\w+\s*\|\s*(?<command>/.*)$")]
    private static partial Regex ListingLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Reads the listing; lines that are not favorites (notes, blanks) are ignored.</summary>
    public static List<RdpImportEntry> ParseListing(string text) =>
        text.Split('\n')
            .Select(line => ListingLine().Match(line.TrimEnd('\r')))
            .Where(match => match.Success)
            .Select(match => ParseFavorite(match.Groups["name"].Value, match.Groups["command"].Value))
            .ToList();

    public static RdpImportEntry ParseFavorite(string name, string commandLine)
    {
        var unsupported = new List<string>();
        var parsed = RdpCommandLine.Parse(RdpCommandLine.Tokenize(commandLine), unsupported.Add);
        return new RdpImportEntry(Spaces().Replace(name.Trim(), " "), parsed.Settings, unsupported);
    }

    /// <summary>
    /// Adds the entries as sessions, after the existing ones. One whose name is already taken - by an
    /// existing session or an earlier entry - is skipped, never overwritten.
    /// </summary>
    public static RdpImportResult AddTo(ConfigurationFile config, IEnumerable<RdpImportEntry> entries)
    {
        var added = new List<RdpSession>();
        var skipped = new List<string>();
        foreach (var entry in entries)
        {
            if (config.RdpSessions.Any(session => string.Equals(session.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                skipped.Add(entry.Name);
                continue;
            }

            var session = new RdpSession { Name = entry.Name };
            session.Overlay(entry.Settings);
            config.RdpSessions.Add(session);
            added.Add(session);
        }

        return new RdpImportResult(added, skipped);
    }
}
