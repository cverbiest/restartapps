# PreToolUse hook (Bash and PowerShell tools): before a command that runs `git commit`, scan what
# is about to be committed with TruffleHog and block the command (exit 2) if it finds anything.
#
# Scanned: the staged content (the index, not the working file), plus changed and untracked files
# that are not staged yet - a command like `git add -A && git commit` stages them only after this
# hook has run. Ignored files are not scanned.

$ErrorActionPreference = 'Stop'

# How to install trufflehog on Windows: the latest release from GitHub, its checksum verified, into
# ~/bin. Shown when trufflehog is missing.
$installHelp = @'
Install trufflehog (Windows, PowerShell): latest release from GitHub, checksum verified, into ~/bin:

    $v = (Invoke-RestMethod https://api.github.com/repos/trufflesecurity/trufflehog/releases/latest).tag_name.TrimStart('v')
    $asset = "trufflehog_${v}_windows_amd64.tar.gz"      # windows_arm64 on ARM
    $base = "https://github.com/trufflesecurity/trufflehog/releases/download/v$v"
    $tgz = Join-Path $env:TEMP $asset
    Invoke-WebRequest "$base/$asset" -OutFile $tgz
    $sum = ((Invoke-RestMethod "$base/trufflehog_${v}_checksums.txt") -split "`n" | Where-Object { $_ -like "*  $asset" }) -replace '\s.*$'
    if ((Get-FileHash $tgz -Algorithm SHA256).Hash -ne $sum) { throw "checksum mismatch for $asset" }
    New-Item -ItemType Directory -Force ~/bin | Out-Null
    tar -xzf $tgz -C ~/bin trufflehog.exe
    Remove-Item $tgz

Then put ~/bin on your PATH (once), and restart the terminal / Claude Code:

    [Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ";$HOME\bin", 'User')

Check with: trufflehog --version
On Linux or macOS, take the matching linux_/darwin_ amd64/arm64 .tar.gz from the same release and
check it with: sha256sum -c --ignore-missing trufflehog_<version>_checksums.txt
'@

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$command = [string]$payload.tool_input.command
if ($command -notmatch '\bgit\b[^;&|]*\bcommit\b') {
    exit 0
}

$trufflehog = (Get-Command trufflehog -ErrorAction SilentlyContinue).Source
if (-not $trufflehog) {
    [Console]::Error.WriteLine('Commit blocked: trufflehog is not on PATH, so what would be committed cannot be scanned for secrets.')
    [Console]::Error.WriteLine($installHelp)
    exit 2
}

# A clean scan lets the command run and shows its result: to the user in the UI, and to Claude, who
# includes it when reporting the commit.
function Report([string]$message) {
    @{
        systemMessage      = $message
        hookSpecificOutput = @{ hookEventName = 'PreToolUse'; additionalContext = "Secret scan before this commit: $message" }
    } | ConvertTo-Json -Compress
}

Set-Location (git rev-parse --show-toplevel)
$scan = Join-Path ([IO.Path]::GetTempPath()) "scan-staged-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scan | Out-Null
try {
    # git prints paths with / and quotes unusual ones unless -z is used.
    function Paths([string[]]$arguments) {
        # Warnings (line endings) go to stderr; only the paths are wanted.
        ((& git @arguments 2>$null) -join "`0").Split("`0", [StringSplitOptions]::RemoveEmptyEntries)
    }

    $staged = Paths @('diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z')
    foreach ($path in $staged) {
        $target = Join-Path $scan "staged/$path"
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        # Through cmd so the blob's bytes reach the file unchanged (PowerShell would re-encode them).
        cmd /c "git show `":$path`" > `"$target`""
    }

    $unstaged = @(Paths @('diff', '--name-only', '--diff-filter=ACMR', '-z')) + @(Paths @('ls-files', '--others', '--exclude-standard', '-z'))
    foreach ($path in $unstaged | Sort-Object -Unique) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $target = Join-Path $scan "working/$path"
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath $path -Destination $target
        }
    }

    $files = @(Get-ChildItem $scan -Recurse -File)
    if ($files.Count -eq 0) {
        Report 'trufflehog: nothing to scan (no staged, changed or untracked files).'
        exit 0
    }

    # Results go to stdout as JSON lines, trufflehog's own log (with its summary) to stderr.
    $all = & $trufflehog filesystem $scan --no-update --json --fail 2>&1
    $exitCode = $LASTEXITCODE
    $output = @($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })
    if ($exitCode -eq 0) {
        $stagedCount = @($files | Where-Object { $_.FullName.StartsWith((Join-Path $scan 'staged')) }).Count
        $summary = $all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] -and "$_" -match '"finished scanning"' } |
            Select-Object -Last 1 | ForEach-Object { "$_" | ConvertFrom-Json }
        $detail = if ($summary) { ", $($summary.bytes) bytes, trufflehog $($summary.trufflehog_version)" } else { '' }
        Report "trufflehog: 0 findings in $($files.Count) file(s) ($stagedCount staged, $($files.Count - $stagedCount) not staged yet$detail)."
        exit 0
    }

    $findings = @($output | Where-Object { $_ -match '"DetectorName"' } | ForEach-Object {
        $result = $_ | ConvertFrom-Json
        $file = $result.SourceMetadata.Data.Filesystem.file.Substring($scan.Length + 1) -replace '\\', '/'
        $raw = [string]$result.Raw
        $shown = if ($raw.Length -gt 8) { "$($raw.Substring(0, 4))...$($raw.Substring($raw.Length - 4))" } else { '(short)' }
        "  $($result.DetectorName) in $file line $($result.SourceMetadata.Data.Filesystem.line), verified: $($result.Verified), $shown"
    })

    if ($findings.Count -eq 0) {
        [Console]::Error.WriteLine("Commit blocked: trufflehog exited with code $exitCode without findings; run it by hand to see why.")
        exit 2
    }

    [Console]::Error.WriteLine("Commit blocked: trufflehog found possible secrets in what would be committed (staged/ = the index, working/ = not staged yet):")
    $findings | ForEach-Object { [Console]::Error.WriteLine($_) }
    [Console]::Error.WriteLine('Remove them, or show the findings to the user and let them decide. A verified secret must be rotated.')
    exit 2
}
finally {
    Remove-Item -Recurse -Force $scan -ErrorAction SilentlyContinue
}
