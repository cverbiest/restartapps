using System.Text;

namespace DevWorkspaceLauncher;

/// <summary>A connectable entry of <c>~/.ssh/config</c>: a <c>Host</c> line whose first pattern has no wildcard.</summary>
public sealed record SshHostEntry(
    string Alias,
    IReadOnlyList<string> OtherPatterns,
    string? HostName,
    string? User,
    string? IdentityFile,
    IReadOnlyList<string> MoreIdentityFiles);

/// <summary>The fields the SSH hosts tab edits. Null or blank means "not set".</summary>
public sealed record SshHostFields(string Alias, string? HostName, string? User, string? IdentityFile);

/// <summary>
/// <c>~/.ssh/config</c> as its lines, so that a save changes only the entries that were edited: comments,
/// blank lines, indentation, keyword case, <c>Match</c> and <c>Include</c> lines are written back as they
/// were read.
/// <para>
/// An entry starts at a <c>Host</c> or <c>Match</c> line - or at the comment lines directly above it -
/// and runs to the start of the next one.
/// </para>
/// </summary>
public sealed class SshConfigDocument
{
    private static readonly string[] EditedKeywords = ["HostName", "User", "IdentityFile"];

    private readonly List<string> _lines;
    private readonly string _newLine;
    private bool _endsWithNewLine;

    private SshConfigDocument(List<string> lines, string newLine, bool endsWithNewLine)
    {
        _lines = lines;
        _newLine = newLine;
        _endsWithNewLine = endsWithNewLine;
    }

    public static SshConfigDocument Parse(string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Length == 0 ? [] : text.Split('\n').ToList();
        var endsWithNewLine = lines.Count > 0 && lines[^1].Length == 0;
        if (endsWithNewLine)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (newLine == "\r\n")
        {
            lines = lines.Select(line => line.EndsWith('\r') ? line[..^1] : line).ToList();
        }

        return new SshConfigDocument(lines, newLine, endsWithNewLine);
    }

    public string ToText() => string.Join(_newLine, _lines) + (_endsWithNewLine && _lines.Count > 0 ? _newLine : string.Empty);

    /// <summary>An alias ssh can be asked to connect to: one word, no wildcard or negation.</summary>
    public static bool IsValidAlias(string alias) =>
        alias.Length > 0 && !alias.Any(c => char.IsWhiteSpace(c) || c is '*' or '?' or '!' or '"' or '#');

    public IReadOnlyList<SshHostEntry> Hosts =>
        Blocks().Where(block => block.Alias is not null).Select(ToEntry).ToList();

    /// <summary>Writes the fields into the entry named <paramref name="alias"/>; false when there is none.</summary>
    public bool Update(string alias, SshHostFields fields)
    {
        if (Find(alias) is not { } block)
        {
            return false;
        }

        if (!string.Equals(block.Alias, fields.Alias, StringComparison.Ordinal))
        {
            var host = ParseSetting(_lines[block.HostLine])!.Value;
            var patterns = SplitPatterns(host.Value);
            patterns[0] = fields.Alias;
            _lines[block.HostLine] = host.Indent + host.Keyword + host.Separator + string.Join(' ', patterns);
        }

        foreach (var keyword in EditedKeywords)
        {
            // Re-find the entry each time: a removed or inserted line moves the ones after it.
            SetValue(Find(fields.Alias)!, keyword, Value(fields, keyword));
        }

        return true;
    }

    /// <summary>Adds an entry at the end, but before trailing defaults (<c>Host *</c>, <c>Match</c>) that would override it.</summary>
    public void Add(SshHostFields fields)
    {
        var indent = DefaultIndent();
        var entry = new List<string> { $"Host {fields.Alias}" };
        entry.AddRange(EditedKeywords
            .Where(keyword => !string.IsNullOrWhiteSpace(Value(fields, keyword)))
            .Select(keyword => indent + keyword + " " + Format(Value(fields, keyword)!.Trim(), quoted: false)));

        var defaults = Blocks().AsEnumerable().Reverse().TakeWhile(block => block.Alias is null).LastOrDefault();
        if (defaults is null)
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
            {
                _lines.Add(string.Empty);
            }

            _lines.AddRange(entry);
            _endsWithNewLine = true;
            return;
        }

        var at = defaults.Start;
        if (at > 0 && !string.IsNullOrWhiteSpace(_lines[at - 1]))
        {
            _lines.Insert(at++, string.Empty);
        }

        entry.Add(string.Empty);
        _lines.InsertRange(at, entry);
    }

    /// <summary>Removes the entry with the comment lines above it; false when there is none.</summary>
    public bool Remove(string alias)
    {
        if (Find(alias) is not { } block)
        {
            return false;
        }

        _lines.RemoveRange(block.Start, block.End - block.Start);

        // The last entry: drop the blank line that separated it from the one before.
        if (block.Start == _lines.Count)
        {
            while (_lines.Count > 0 && string.IsNullOrWhiteSpace(_lines[^1]))
            {
                _lines.RemoveAt(_lines.Count - 1);
            }
        }

        return true;
    }

    private Block? Find(string alias) =>
        Blocks().FirstOrDefault(block => string.Equals(block.Alias, alias, StringComparison.OrdinalIgnoreCase));

    private void SetValue(Block block, string keyword, string? value)
    {
        var existing = SettingLines(block)
            .FirstOrDefault(index => string.Equals(ParseSetting(_lines[index])!.Value.Keyword, keyword, StringComparison.OrdinalIgnoreCase), -1);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing >= 0)
            {
                _lines.RemoveAt(existing);
            }

            return;
        }

        value = value.Trim();
        if (existing >= 0)
        {
            var setting = ParseSetting(_lines[existing])!.Value;
            if (Unquote(setting.Value) != value)
            {
                _lines[existing] = setting.Indent + setting.Keyword + setting.Separator + Format(value, setting.Value.StartsWith('"'));
            }

            return;
        }

        var settings = SettingLines(block).ToList();
        var indent = settings.Count > 0 ? ParseSetting(_lines[settings[0]])!.Value.Indent : DefaultIndent();
        var after = settings.Count > 0 ? settings[^1] : block.HostLine;
        _lines.Insert(after + 1, indent + keyword + " " + Format(value, quoted: false));
    }

    /// <summary>The line numbers of an entry's settings, after its Host line.</summary>
    private IEnumerable<int> SettingLines(Block block) =>
        Enumerable.Range(block.HostLine + 1, block.End - block.HostLine - 1).Where(index => ParseSetting(_lines[index]) is not null);

    /// <summary>The indentation of the first setting in any Host entry, or four spaces.</summary>
    private string DefaultIndent() =>
        Blocks().SelectMany(SettingLines).Select(index => ParseSetting(_lines[index])!.Value.Indent).FirstOrDefault(indent => indent.Length > 0)
        ?? "    ";

    private SshHostEntry ToEntry(Block block)
    {
        var values = SettingLines(block).Select(index => ParseSetting(_lines[index])!.Value).ToList();
        string? First(string keyword) => values.Where(setting => Is(setting, keyword)).Select(setting => Unquote(setting.Value)).FirstOrDefault();
        var identityFiles = values.Where(setting => Is(setting, "IdentityFile")).Select(setting => Unquote(setting.Value)).ToList();

        return new SshHostEntry(
            block.Alias!,
            block.Patterns.Skip(1).ToList(),
            First("HostName"),
            First("User"),
            identityFiles.FirstOrDefault(),
            identityFiles.Skip(1).ToList());

        static bool Is(Setting setting, string keyword) => string.Equals(setting.Keyword, keyword, StringComparison.OrdinalIgnoreCase);
    }

    private List<Block> Blocks()
    {
        var starts = new List<int>();
        for (var index = 0; index < _lines.Count; index++)
        {
            if (ParseSetting(_lines[index]) is { } setting && IsBlockKeyword(setting.Keyword))
            {
                starts.Add(index);
            }
        }

        var blocks = new List<Block>();
        for (var i = 0; i < starts.Count; i++)
        {
            var hostLine = starts[i];
            var start = hostLine;
            var floor = i == 0 ? 0 : starts[i - 1] + 1;
            while (start > floor && _lines[start - 1].TrimStart().StartsWith('#'))
            {
                start--;
            }

            blocks.Add(new Block(start, hostLine, _lines.Count, Patterns(hostLine)));
            if (i > 0)
            {
                blocks[i - 1] = blocks[i - 1] with { End = start };
            }
        }

        return blocks;

        List<string> Patterns(int line)
        {
            var setting = ParseSetting(_lines[line])!.Value;
            return string.Equals(setting.Keyword, "Host", StringComparison.OrdinalIgnoreCase) ? SplitPatterns(setting.Value) : [];
        }

        static bool IsBlockKeyword(string keyword) =>
            string.Equals(keyword, "Host", StringComparison.OrdinalIgnoreCase) || string.Equals(keyword, "Match", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Value(SshHostFields fields, string keyword) => keyword switch
    {
        "HostName" => fields.HostName,
        "User" => fields.User,
        _ => fields.IdentityFile
    };

    private static string Format(string value, bool quoted) =>
        quoted || value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static List<string> SplitPatterns(string value)
    {
        var patterns = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in value)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return patterns;

        void Flush()
        {
            if (current.Length > 0)
            {
                patterns.Add(current.ToString());
                current.Clear();
            }
        }
    }

    /// <summary>A <c>Keyword value</c> or <c>Keyword=value</c> line, split so it can be put back together; null for a blank or comment line.</summary>
    private static Setting? ParseSetting(string line)
    {
        var text = line.TrimStart();
        if (text.Length == 0 || text[0] == '#')
        {
            return null;
        }

        var indent = line[..^text.Length];
        var keywordLength = 0;
        while (keywordLength < text.Length && !char.IsWhiteSpace(text[keywordLength]) && text[keywordLength] != '=')
        {
            keywordLength++;
        }

        var rest = text[keywordLength..];
        var separatorLength = 0;
        while (separatorLength < rest.Length && char.IsWhiteSpace(rest[separatorLength]))
        {
            separatorLength++;
        }

        if (separatorLength < rest.Length && rest[separatorLength] == '=')
        {
            separatorLength++;
            while (separatorLength < rest.Length && char.IsWhiteSpace(rest[separatorLength]))
            {
                separatorLength++;
            }
        }

        return new Setting(indent, text[..keywordLength], rest[..separatorLength], rest[separatorLength..].TrimEnd());
    }

    private readonly record struct Setting(string Indent, string Keyword, string Separator, string Value);

    /// <summary>
    /// One entry: <see cref="Start"/> is its first line (a comment above it, or the Host line),
    /// <see cref="End"/> the first line after it. <see cref="Alias"/> is null for Match and wildcard entries.
    /// </summary>
    private sealed record Block(int Start, int HostLine, int End, List<string> Patterns)
    {
        public string? Alias => Patterns is [var first, ..] && IsValidAlias(first) ? first : null;
    }
}
