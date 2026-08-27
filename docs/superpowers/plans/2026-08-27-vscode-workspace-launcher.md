# VS Code Workspace Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Workspaces tab to Restart Apps that lists preferred VS Code workspaces, shows which ones VS Code currently has open, and launches or focuses them with one click.

**Architecture:** Workspaces are discovered by scanning `%APPDATA%\Code\User\workspaceStorage\*\workspace.json` (directory mtime gives recency); a workspace counts as open when its `state.vscdb` cannot be opened with `FileShare.None`, because the VS Code window holding it keeps a lock; launching shells out to `Code.exe --folder-uri` / `--file-uri`, which VS Code answers by focusing an existing window. All of that logic is pure and lives in a new `VsCode.cs` covered by a new xUnit project; the WinForms layer stays untested and merely binds it into a `KryptonNavigator` tab.

**Tech Stack:** .NET 10 (`net10.0-windows`), WinForms, Krypton.Toolkit 105.26.4.110, Krypton.Navigator 105.26.4.110, System.Text.Json, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-08-26-vscode-workspace-launcher-design.md`

## Global Constraints

- Target framework stays `net10.0-windows`; the app stays Windows-only WinForms.
- Only two new dependencies are allowed: `Krypton.Navigator` **version 105.26.4.110** (must match the existing `Krypton.Toolkit` version exactly) in the app, and the xUnit test packages in the test project. No SQLite package.
- Everything in the app is `internal`; the test project reaches it through an `InternalsVisibleTo` attribute, not by widening accessibility to `public`.
- `restartapps.json` stays the source of truth. The `Workspaces` property is optional on read so existing configuration files load unchanged.
- Discovery, the open probe and executable resolution never throw: a failure is reported as "not found" / "not open" and logged, never as an exception reaching the UI.
- Never stop, close or kill a VS Code process. This feature only reads state and starts processes.
- The app is only ever asked about `%APPDATA%\Code` (VS Code Stable). Insiders and forks are out of scope.
- Existing behaviour must not regress: the Apps grid, profiles, one-click buttons, drag-and-drop, config file watcher and log pane all keep working exactly as they do today.
- Match the surrounding code style: file-scoped namespace, `internal` types, XML doc comments on non-obvious members, comments that explain *why*, collection expressions (`[]`), `is not null` patterns.

---

### Task 1: Test project and VS Code URI parsing

Creates the test project (needed by every later task) and the pure URI decoder that turns a VS Code URI into a kind, a display name and a location label.

**Files:**
- Create: `RestartApps.Tests/RestartApps.Tests.csproj`
- Create: `RestartApps.Tests/VsCodeUriTests.cs`
- Create: `VsCode.cs`
- Modify: `RestartApps.csproj` (add the `InternalsVisibleTo` attribute)

The existing `.gitignore` already covers `bin/` and `obj/`, so the test project needs no change there.

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal enum WorkspaceKind { Folder, WorkspaceFile }`
  - `internal sealed record VsCodeWorkspace(string Id, string Uri, WorkspaceKind Kind, string DisplayName, string Location, DateTime LastUsed)`
  - `internal static class VsCodeUri` with `public static (WorkspaceKind Kind, string DisplayName, string Location) Describe(string uri)`

- [ ] **Step 1: Create the test project**

```powershell
dotnet new xunit -o RestartApps.Tests
```

Then replace the generated `RestartApps.Tests/RestartApps.Tests.csproj` with exactly this (the template targets plain `net10.0`, which cannot reference a WinForms project, and the generated `UnitTest1.cs` must be deleted):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RestartApps.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

```powershell
Remove-Item RestartApps.Tests\UnitTest1.cs
```

- [ ] **Step 2: Grant the test project access to internals**

Add this item group to `RestartApps.csproj`, after the existing `PackageReference` group:

```xml
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>RestartApps.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `RestartApps.Tests/VsCodeUriTests.cs`:

```csharp
namespace RestartApps.Tests;

public class VsCodeUriTests
{
    [Fact]
    public void Describes_a_local_folder()
    {
        var (kind, name, location) = VsCodeUri.Describe("file:///c%3A/cce/workspaces/restartapps");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("restartapps", name);
        Assert.Equal(@"c:\cce\workspaces\restartapps", location);
    }

    [Fact]
    public void Describes_a_local_workspace_file()
    {
        var (kind, name, location) = VsCodeUri.Describe("file:///c%3A/cce/workspaces/develop/develop.code-workspace");

        Assert.Equal(WorkspaceKind.WorkspaceFile, kind);
        Assert.Equal("develop", name);
        Assert.Equal(@"c:\cce\workspaces\develop\develop.code-workspace", location);
    }

    [Fact]
    public void Describes_an_ssh_remote_workspace_file()
    {
        var (kind, name, location) = VsCodeUri.Describe(
            "vscode-remote://ssh-remote%2Bcce-dev03_cvb/home/cce/cvb/workspaces/develop/develop.code-workspace");

        Assert.Equal(WorkspaceKind.WorkspaceFile, kind);
        Assert.Equal("develop", name);
        Assert.Equal("SSH: cce-dev03_cvb", location);
    }

    [Fact]
    public void Describes_a_wsl_folder()
    {
        var (kind, name, location) = VsCodeUri.Describe("vscode-remote://wsl%2BUbuntu/home/cvb/code/b1-cce-poc");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("b1-cce-poc", name);
        Assert.Equal("WSL: Ubuntu", location);
    }

    [Fact]
    public void Describes_a_dev_container_folder_without_decoding_its_authority()
    {
        var (kind, name, location) = VsCodeUri.Describe(
            "vscode-remote://dev-container%2B7b22686f737450617468223a222782f79227d/workspaces/b1-cce-poc");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("b1-cce-poc", name);
        Assert.Equal("Dev Container", location);
    }

    [Fact]
    public void Ignores_a_trailing_slash_on_a_folder()
    {
        var (_, name, _) = VsCodeUri.Describe("file:///c%3A/cce/workspaces/restartapps/");

        Assert.Equal("restartapps", name);
    }

    [Fact]
    public void Falls_back_to_the_raw_authority_for_an_unknown_remote()
    {
        var (_, _, location) = VsCodeUri.Describe("vscode-remote://codespaces%2Bfoo/workspaces/bar");

        Assert.Equal("codespaces+foo", location);
    }

    [Fact]
    public void Survives_a_uri_it_cannot_parse()
    {
        var (kind, name, location) = VsCodeUri.Describe("not-a-uri");

        Assert.Equal(WorkspaceKind.Folder, kind);
        Assert.Equal("not-a-uri", name);
        Assert.Equal("not-a-uri", location);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: FAIL — compile errors, `VsCodeUri` and `WorkspaceKind` do not exist.

- [ ] **Step 5: Write the implementation**

Create `VsCode.cs`:

```csharp
namespace RestartApps;

/// <summary>Whether an entry is a plain folder or a .code-workspace file.</summary>
internal enum WorkspaceKind
{
    Folder,
    WorkspaceFile
}

/// <summary>One workspace VS Code has opened at least once.</summary>
/// <param name="Id">The workspaceStorage directory name, used to probe whether it is open.</param>
/// <param name="Uri">The URI exactly as VS Code stored it; this is what gets handed back to VS Code.</param>
/// <param name="Location">A local path, or a remote label such as "SSH: host".</param>
/// <param name="LastUsed">The storage directory's last write time, which is when it was last opened.</param>
internal sealed record VsCodeWorkspace(
    string Id,
    string Uri,
    WorkspaceKind Kind,
    string DisplayName,
    string Location,
    DateTime LastUsed);

/// <summary>
/// Decodes the URIs VS Code stores. Parsed by hand rather than through <see cref="Uri"/> because the
/// remote authority carries percent-escaped '+' separators that the framework parser does not
/// unescape consistently.
/// </summary>
internal static class VsCodeUri
{
    private const string WorkspaceExtension = ".code-workspace";

    public static (WorkspaceKind Kind, string DisplayName, string Location) Describe(string uri)
    {
        var (scheme, authority, path) = Split(uri);

        var kind = path.EndsWith(WorkspaceExtension, StringComparison.OrdinalIgnoreCase)
            ? WorkspaceKind.WorkspaceFile
            : WorkspaceKind.Folder;

        var name = LastSegment(path);
        if (kind == WorkspaceKind.WorkspaceFile)
        {
            name = name[..^WorkspaceExtension.Length];
        }

        var location = string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase)
            ? ToWindowsPath(path)
            : RemoteLabel(authority);

        return (kind, name, location);
    }

    private static (string Scheme, string Authority, string Path) Split(string uri)
    {
        var mark = uri.IndexOf("://", StringComparison.Ordinal);
        if (mark < 0)
        {
            return (string.Empty, string.Empty, Unescape(uri));
        }

        var scheme = uri[..mark];
        var rest = uri[(mark + 3)..];
        var slash = rest.IndexOf('/');

        return slash < 0
            ? (scheme, Unescape(rest), string.Empty)
            : (scheme, Unescape(rest[..slash]), Unescape(rest[slash..]));
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            // A malformed escape is not worth failing over; the raw text still identifies the entry.
            return value;
        }
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string ToWindowsPath(string path)
    {
        return path.TrimStart('/').TrimEnd('/').Replace('/', '\\');
    }

    private static string RemoteLabel(string authority)
    {
        var plus = authority.IndexOf('+');
        if (plus < 0)
        {
            return authority;
        }

        var value = authority[(plus + 1)..];
        return authority[..plus].ToLowerInvariant() switch
        {
            "ssh-remote" => $"SSH: {value}",
            "wsl" => $"WSL: {value}",
            // The dev container authority is a hex-encoded JSON blob; showing it would be noise.
            "dev-container" => "Dev Container",
            "attached-container" => "Container",
            _ => authority
        };
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 8 tests.

- [ ] **Step 7: Commit**

```bash
git add RestartApps.Tests RestartApps.csproj VsCode.cs
git commit -m "Add VS Code URI decoding and a test project"
```

---

### Task 2: Workspace discovery

Scans `workspaceStorage` and returns the workspaces VS Code knows about, most recently used first.

**Files:**
- Modify: `VsCode.cs` (add `VsCodeWorkspaces`)
- Create: `RestartApps.Tests/VsCodeWorkspacesTests.cs`

**Interfaces:**
- Consumes: `VsCodeUri.Describe`, `VsCodeWorkspace`, `WorkspaceKind` from Task 1.
- Produces: `internal static class VsCodeWorkspaces` with
  - `public static string DefaultStorageRoot { get; }`
  - `public static IReadOnlyList<VsCodeWorkspace> Discover(string storageRoot)`

- [ ] **Step 1: Write the failing tests**

Create `RestartApps.Tests/VsCodeWorkspacesTests.cs`:

```csharp
namespace RestartApps.Tests;

public class VsCodeWorkspacesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "restartapps-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    /// <summary>Writes one workspaceStorage entry and stamps the directory so ordering is deterministic.</summary>
    private string WriteEntry(string id, string json, DateTime lastUsedUtc)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "workspace.json"), json);
        Directory.SetLastWriteTimeUtc(directory, lastUsedUtc);
        return directory;
    }

    [Fact]
    public void Returns_nothing_when_the_storage_root_does_not_exist()
    {
        Assert.Empty(VsCodeWorkspaces.Discover(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Reads_folder_and_workspace_entries()
    {
        WriteEntry("aaa", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("bbb", """{ "workspace": "file:///c%3A/code/beta/beta.code-workspace" }""", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var workspaces = VsCodeWorkspaces.Discover(_root);

        Assert.Equal(2, workspaces.Count);

        // Most recently used first.
        Assert.Equal("beta", workspaces[0].DisplayName);
        Assert.Equal("bbb", workspaces[0].Id);
        Assert.Equal(WorkspaceKind.WorkspaceFile, workspaces[0].Kind);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), workspaces[0].LastUsed);

        Assert.Equal("alpha", workspaces[1].DisplayName);
        Assert.Equal(WorkspaceKind.Folder, workspaces[1].Kind);
    }

    [Fact]
    public void Keeps_the_newest_of_two_entries_for_the_same_uri()
    {
        WriteEntry("old", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("new", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        var workspace = Assert.Single(VsCodeWorkspaces.Discover(_root));

        Assert.Equal("new", workspace.Id);
    }

    [Fact]
    public void Skips_entries_it_cannot_use()
    {
        Directory.CreateDirectory(Path.Combine(_root, "no-json"));
        WriteEntry("broken", "{ not json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("empty-uri", """{ "folder": "" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("wrong-shape", """{ "folder": 42 }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteEntry("good", """{ "folder": "file:///c%3A/code/alpha" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var workspace = Assert.Single(VsCodeWorkspaces.Discover(_root));

        Assert.Equal("good", workspace.Id);
    }

    [Fact]
    public void Points_at_the_real_vs_code_storage_by_default()
    {
        Assert.EndsWith(Path.Combine("Code", "User", "workspaceStorage"), VsCodeWorkspaces.DefaultStorageRoot);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj --filter FullyQualifiedName~VsCodeWorkspacesTests`
Expected: FAIL — `VsCodeWorkspaces` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `VsCode.cs`, after `VsCodeUri`, and add `using System.Text.Json;` at the top of the file:

```csharp
/// <summary>
/// Reads the workspaces VS Code has opened. VS Code keeps one directory per workspace under
/// workspaceStorage, holding a workspace.json that names it; the directory's last write time is when
/// it was last opened. That is used instead of the recent-paths list in state.vscdb, which is a
/// SQLite database and is not present on every installation.
/// </summary>
internal static class VsCodeWorkspaces
{
    public static string DefaultStorageRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Code",
        "User",
        "workspaceStorage");

    /// <summary>Every workspace VS Code knows about, most recently used first.</summary>
    public static IReadOnlyList<VsCodeWorkspace> Discover(string storageRoot)
    {
        if (!Directory.Exists(storageRoot))
        {
            return [];
        }

        var found = new Dictionary<string, VsCodeWorkspace>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(storageRoot);
        }
        catch
        {
            return [];
        }

        foreach (var directory in directories)
        {
            var workspace = ReadEntry(directory);
            if (workspace is null)
            {
                continue;
            }

            // The same folder can have several storage directories; the newest one is the live one.
            if (found.TryGetValue(workspace.Uri, out var existing) && existing.LastUsed >= workspace.LastUsed)
            {
                continue;
            }

            found[workspace.Uri] = workspace;
        }

        return found.Values
            .OrderByDescending(workspace => workspace.LastUsed)
            .ThenBy(workspace => workspace.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static VsCodeWorkspace? ReadEntry(string directory)
    {
        var file = Path.Combine(directory, "workspace.json");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var uri = ReadUri(document.RootElement);
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            var (kind, name, location) = VsCodeUri.Describe(uri);
            return new VsCodeWorkspace(
                Path.GetFileName(directory),
                uri,
                kind,
                name,
                location,
                Directory.GetLastWriteTimeUtc(directory));
        }
        catch
        {
            // An unreadable or half-written entry is skipped rather than failing the whole scan.
            return null;
        }
    }

    private static string? ReadUri(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.String)
        {
            return folder.GetString();
        }

        return root.TryGetProperty("workspace", out var workspace) && workspace.ValueKind == JsonValueKind.String
            ? workspace.GetString()
            : null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add VsCode.cs RestartApps.Tests
git commit -m "Discover VS Code workspaces from workspaceStorage"
```

---

### Task 3: Open-window detection

Answers "does a VS Code window have this workspace open right now?" by probing the lock VS Code holds on the workspace's `state.vscdb`.

**Files:**
- Modify: `VsCode.cs` (add two members to `VsCodeWorkspaces`)
- Create: `RestartApps.Tests/VsCodeOpenProbeTests.cs`

**Interfaces:**
- Consumes: `VsCodeWorkspaces` from Task 2.
- Produces:
  - `public static bool VsCodeWorkspaces.IsRunning()`
  - `public static bool VsCodeWorkspaces.IsOpen(string storageRoot, string id)`

- [ ] **Step 1: Write the failing tests**

Create `RestartApps.Tests/VsCodeOpenProbeTests.cs`:

```csharp
namespace RestartApps.Tests;

public class VsCodeOpenProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "restartapps-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private string WriteDatabase(string id)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "state.vscdb");
        File.WriteAllText(file, "not really sqlite");
        return file;
    }

    [Fact]
    public void Reports_not_open_when_the_database_is_missing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        Assert.False(VsCodeWorkspaces.IsOpen(_root, "empty"));
    }

    [Fact]
    public void Reports_not_open_when_nothing_holds_the_database()
    {
        WriteDatabase("closed");

        Assert.False(VsCodeWorkspaces.IsOpen(_root, "closed"));
    }

    [Fact]
    public void Reports_open_while_another_handle_holds_the_database()
    {
        var file = WriteDatabase("live");

        // This is how SQLite - and so a live VS Code window - holds the file: readable and writable
        // by others, which still defeats an exclusive open.
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            Assert.True(VsCodeWorkspaces.IsOpen(_root, "live"));
        }

        Assert.False(VsCodeWorkspaces.IsOpen(_root, "live"));
    }

    [Fact]
    public void Reports_not_open_for_an_unknown_id()
    {
        Assert.False(VsCodeWorkspaces.IsOpen(_root, "no-such-id"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj --filter FullyQualifiedName~VsCodeOpenProbeTests`
Expected: FAIL — `IsOpen` does not exist.

- [ ] **Step 3: Write the implementation**

Add these two members to `VsCodeWorkspaces` in `VsCode.cs`, and add `using System.Diagnostics;` at the top of the file:

```csharp
    /// <summary>Whether VS Code is running at all, so a whole refresh can skip the disk when it is not.</summary>
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("Code");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>
    /// Whether a VS Code window currently has this workspace open. A live window keeps its
    /// workspace state database open, so an exclusive open of that file fails exactly while the
    /// window exists. Window titles cannot answer this: window.title is user-configurable and need
    /// not contain the path at all.
    /// </summary>
    public static bool IsOpen(string storageRoot, string id)
    {
        var file = Path.Combine(storageRoot, id, "state.vscdb");
        if (!File.Exists(file))
        {
            return false;
        }

        try
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            // Read-only or otherwise unopenable: report closed rather than claiming a window exists.
            return false;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 17 tests.

- [ ] **Step 5: Verify the probe against the real VS Code**

This is the one assumption the unit tests cannot prove, so check it by hand once. With at least one VS Code window open, run:

```powershell
$root = "$env:APPDATA\Code\User\workspaceStorage"
Get-ChildItem $root -Directory | ForEach-Object {
  $json = Join-Path $_.FullName 'workspace.json'
  $db = Join-Path $_.FullName 'state.vscdb'
  if ((Test-Path $json) -and (Test-Path $db)) {
    try { $fs = [System.IO.File]::Open($db,'Open','ReadWrite','None'); $fs.Close(); $state = 'free' }
    catch { $state = 'LOCKED' }
    if ($state -eq 'LOCKED') { "$state  $((Get-Content $json -Raw).Trim())" }
  }
}
```

Expected: one LOCKED line per open VS Code window, matching the workspaces actually on screen. If nothing is locked while windows are open, stop and report it — the design's detection method does not hold on this VS Code build.

- [ ] **Step 6: Commit**

```bash
git add VsCode.cs RestartApps.Tests
git commit -m "Detect which workspaces VS Code currently has open"
```

---

### Task 4: Launching VS Code

Resolves the VS Code executable and builds the command that opens — or focuses — a workspace.

**Files:**
- Modify: `VsCode.cs` (add `VsCodeLauncher`)
- Create: `RestartApps.Tests/VsCodeLauncherTests.cs`

**Interfaces:**
- Consumes: `VsCodeWorkspace`, `WorkspaceKind` from Task 1.
- Produces: `internal static class VsCodeLauncher` with
  - `public static string BuildArguments(string uri, WorkspaceKind kind)`
  - `public static IEnumerable<string> CandidatePaths(string? configured)`
  - `public static string? ResolveExecutable(string? configured, Func<string, bool>? exists = null)`
  - `public static ProcessStartInfo CreateStartInfo(string executable, string arguments)`

- [ ] **Step 1: Write the failing tests**

Create `RestartApps.Tests/VsCodeLauncherTests.cs`:

```csharp
namespace RestartApps.Tests;

public class VsCodeLauncherTests
{
    [Fact]
    public void Opens_a_folder_with_folder_uri()
    {
        Assert.Equal(
            "--folder-uri \"file:///c%3A/code/alpha\"",
            VsCodeLauncher.BuildArguments("file:///c%3A/code/alpha", WorkspaceKind.Folder));
    }

    [Fact]
    public void Opens_a_workspace_file_with_file_uri()
    {
        Assert.Equal(
            "--file-uri \"file:///c%3A/code/beta/beta.code-workspace\"",
            VsCodeLauncher.BuildArguments("file:///c%3A/code/beta/beta.code-workspace", WorkspaceKind.WorkspaceFile));
    }

    [Fact]
    public void Prefers_the_configured_executable()
    {
        var resolved = VsCodeLauncher.ResolveExecutable(@"C:\tools\Code.exe", path => true);

        Assert.Equal(@"C:\tools\Code.exe", resolved);
    }

    [Fact]
    public void Expands_environment_variables_in_the_configured_executable()
    {
        var resolved = VsCodeLauncher.ResolveExecutable(@"%SystemRoot%\Code.exe", path => true);

        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "Code.exe"), resolved);
    }

    [Fact]
    public void Falls_back_to_the_per_user_install_when_nothing_is_configured()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "Code.exe");

        var resolved = VsCodeLauncher.ResolveExecutable(null, path => path == expected);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Skips_a_configured_executable_that_is_not_there()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft VS Code",
            "Code.exe");

        var resolved = VsCodeLauncher.ResolveExecutable(@"C:\gone\Code.exe", path => path == expected);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Looks_for_the_cli_shim_on_the_path()
    {
        Assert.Contains(VsCodeLauncher.CandidatePaths(null), candidate => candidate.EndsWith("code.cmd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Returns_null_when_no_candidate_exists()
    {
        Assert.Null(VsCodeLauncher.ResolveExecutable(null, path => false));
    }

    [Fact]
    public void Starts_an_exe_directly()
    {
        var info = VsCodeLauncher.CreateStartInfo(@"C:\tools\Code.exe", "--folder-uri \"x\"");

        Assert.Equal(@"C:\tools\Code.exe", info.FileName);
        Assert.Equal("--folder-uri \"x\"", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
    }

    [Fact]
    public void Starts_a_cmd_shim_through_the_command_processor()
    {
        var info = VsCodeLauncher.CreateStartInfo(@"C:\tools\code.cmd", "--folder-uri \"x\"");

        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal("/c \"\"C:\\tools\\code.cmd\" --folder-uri \"x\"\"", info.Arguments);
        Assert.True(info.CreateNoWindow);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj --filter FullyQualifiedName~VsCodeLauncherTests`
Expected: FAIL — `VsCodeLauncher` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `VsCode.cs`, after `VsCodeWorkspaces`:

```csharp
/// <summary>
/// Builds the command that opens a workspace. VS Code answers a request for a workspace it already
/// has open by focusing that window instead of opening a second one, so the same command serves both
/// the Launch and the Focus button.
/// </summary>
internal static class VsCodeLauncher
{
    public static string BuildArguments(string uri, WorkspaceKind kind)
    {
        return kind == WorkspaceKind.WorkspaceFile
            ? $"--file-uri \"{uri}\""
            : $"--folder-uri \"{uri}\"";
    }

    /// <summary>Where VS Code might be, best first.</summary>
    public static IEnumerable<string> CandidatePaths(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Environment.ExpandEnvironmentVariables(configured);
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "Code.exe");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft VS Code",
            "Code.exe");

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, "code.cmd");
        }
    }

    /// <param name="exists">Injected so the resolution order can be tested without a VS Code install.</param>
    public static string? ResolveExecutable(string? configured, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        foreach (var candidate in CandidatePaths(configured))
        {
            try
            {
                if (exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // An unusable PATH entry just means this candidate is not the one.
            }
        }

        return null;
    }

    public static ProcessStartInfo CreateStartInfo(string executable, string arguments)
    {
        // A .cmd shim is not an executable image, so it has to go through the command processor.
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{executable}\" {arguments}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 27 tests.

- [ ] **Step 5: Commit**

```bash
git add VsCode.cs RestartApps.Tests
git commit -m "Resolve VS Code and build its launch command"
```

---

### Task 5: Configuration

Adds the `Workspaces` list and the optional `VsCodeExecutable` override to the configuration file, keeping old files loadable.

**Files:**
- Modify: `Configuration.cs`
- Create: `RestartApps.Tests/ConfigurationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal sealed class WorkspaceFavorite { public required string Name { get; set; } public required string Uri { get; set; } }`
  - `ConfigurationFile.Workspaces` (`List<WorkspaceFavorite>`), `ConfigurationFile.VsCodeExecutable` (`string?`)
  - `AppConfigurationStore.Parse(string json)` and `AppConfigurationStore.ToJson(ConfigurationFile)` become `internal` so tests can round-trip without touching disk.

- [ ] **Step 1: Write the failing tests**

Create `RestartApps.Tests/ConfigurationTests.cs`:

```csharp
namespace RestartApps.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Reads_a_file_that_predates_workspaces()
    {
        var json = """
        {
          "Apps": [ { "Name": "RDP Plus", "Commandline": "rdp.exe", "Kill": true, "Restart": true } ],
          "Profiles": []
        }
        """;

        var configuration = AppConfigurationStore.Parse(json);

        Assert.Single(configuration.Apps);
        Assert.Empty(configuration.Workspaces);
        Assert.Null(configuration.VsCodeExecutable);
    }

    [Fact]
    public void Reads_the_original_bare_array_layout()
    {
        var json = """[ { "Name": "RDP Plus", "Commandline": "rdp.exe" } ]""";

        var configuration = AppConfigurationStore.Parse(json);

        Assert.Single(configuration.Apps);
        Assert.Empty(configuration.Workspaces);
    }

    [Fact]
    public void Reads_workspaces_and_the_executable_override()
    {
        var json = """
        {
          "Apps": [],
          "Profiles": [],
          "Workspaces": [ { "Name": "develop (linux)", "Uri": "vscode-remote://ssh-remote%2Bhost/home/develop.code-workspace" } ],
          "VsCodeExecutable": "C:\\Program Files\\Microsoft VS Code\\Code.exe"
        }
        """;

        var configuration = AppConfigurationStore.Parse(json);

        var workspace = Assert.Single(configuration.Workspaces);
        Assert.Equal("develop (linux)", workspace.Name);
        Assert.Equal("vscode-remote://ssh-remote%2Bhost/home/develop.code-workspace", workspace.Uri);
        Assert.Equal(@"C:\Program Files\Microsoft VS Code\Code.exe", configuration.VsCodeExecutable);
    }

    [Fact]
    public void Round_trips_workspaces_through_a_save()
    {
        var configuration = new ConfigurationFile
        {
            Workspaces = [new WorkspaceFavorite { Name = "restartapps", Uri = "file:///c%3A/code/restartapps" }]
        };

        var reloaded = AppConfigurationStore.Parse(AppConfigurationStore.ToJson(configuration));

        var workspace = Assert.Single(reloaded.Workspaces);
        Assert.Equal("restartapps", workspace.Name);
        Assert.Equal("file:///c%3A/code/restartapps", workspace.Uri);
    }

    [Fact]
    public void Omits_the_executable_override_when_it_is_not_set()
    {
        var json = AppConfigurationStore.ToJson(new ConfigurationFile());

        Assert.DoesNotContain("VsCodeExecutable", json);
        Assert.Contains("Workspaces", json);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj --filter FullyQualifiedName~ConfigurationTests`
Expected: FAIL — `WorkspaceFavorite`, `Workspaces`, `Parse` and `ToJson` are not accessible.

- [ ] **Step 3: Add the model**

In `Configuration.cs`, add after the `ProfileEntry` class:

```csharp
/// <summary>A VS Code workspace or folder kept on the Workspaces tab.</summary>
internal sealed class WorkspaceFavorite
{
    /// <summary>Shown in the grid. Seeded from the workspace's own name and editable in the file.</summary>
    public required string Name { get; set; }

    /// <summary>The URI as VS Code stores it, which is what identifies and launches the workspace.</summary>
    public required string Uri { get; set; }
}
```

and extend `ConfigurationFile`:

```csharp
internal sealed class ConfigurationFile
{
    public List<AppConfiguration> Apps { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>The preferred VS Code workspaces, in the order they are shown.</summary>
    public List<WorkspaceFavorite> Workspaces { get; set; } = [];

    /// <summary>Overrides where VS Code is looked for. Absent means the usual install locations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VsCodeExecutable { get; set; }
}
```

- [ ] **Step 4: Open the parse and serialize seams**

In `AppConfigurationStore`, change the signature of `Parse` from `private static` to `internal static`, and pull serialization out of `Save` so tests can reach it without writing a file:

```csharp
    /// <summary>Writes the configuration back to disk and returns the JSON that was written.</summary>
    public static string Save(ConfigurationFile configuration)
    {
        var json = ToJson(configuration);
        File.WriteAllText(FilePath, json);
        return json;
    }

    internal static string ToJson(ConfigurationFile configuration)
    {
        return JsonSerializer.Serialize(configuration, WriteOptions);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 32 tests.

- [ ] **Step 6: Commit**

```bash
git add Configuration.cs RestartApps.Tests
git commit -m "Add preferred workspaces to the configuration file"
```

---

### Task 6: Row model for the workspaces grid

The view model the grid binds to: which rows are shown, in what order, and where their names come from. Pure, so it is tested rather than clicked.

**Files:**
- Modify: `VsCode.cs` (add `WorkspaceRow` and `WorkspaceRows`)
- Create: `RestartApps.Tests/WorkspaceRowsTests.cs`

**Interfaces:**
- Consumes: `VsCodeWorkspace`, `WorkspaceKind`, `VsCodeUri.Describe` (Task 1), `WorkspaceFavorite` (Task 5).
- Produces:
  - `internal sealed class WorkspaceRow` with `Uri` (string), `Id` (string?), `Kind` (WorkspaceKind), `Name` (string), `Location` (string), `Preferred` (bool), `IsOpen` (bool, settable)
  - `internal static class WorkspaceRows` with `public static List<WorkspaceRow> Build(IReadOnlyList<VsCodeWorkspace> discovered, IReadOnlyList<WorkspaceFavorite> favorites, bool showAll)`

- [ ] **Step 1: Write the failing tests**

Create `RestartApps.Tests/WorkspaceRowsTests.cs`:

```csharp
namespace RestartApps.Tests;

public class WorkspaceRowsTests
{
    private static VsCodeWorkspace Workspace(string id, string uri, string name, string location, int day)
    {
        return new VsCodeWorkspace(id, uri, WorkspaceKind.Folder, name, location, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
    }

    private static readonly VsCodeWorkspace Alpha = Workspace("a", "file:///c%3A/code/alpha", "alpha", @"c:\code\alpha", 3);
    private static readonly VsCodeWorkspace Beta = Workspace("b", "file:///c%3A/code/beta", "beta", @"c:\code\beta", 2);
    private static readonly VsCodeWorkspace Gamma = Workspace("g", "file:///c%3A/code/gamma", "gamma", @"c:\code\gamma", 1);

    [Fact]
    public void Preferred_view_lists_favourites_in_configuration_order()
    {
        List<WorkspaceFavorite> favorites =
        [
            new() { Name = "gamma", Uri = Gamma.Uri },
            new() { Name = "alpha", Uri = Alpha.Uri }
        ];

        var rows = WorkspaceRows.Build([Alpha, Beta, Gamma], favorites, showAll: false);

        Assert.Equal(["gamma", "alpha"], rows.Select(row => row.Name));
        Assert.All(rows, row => Assert.True(row.Preferred));
        Assert.Equal("g", rows[0].Id);
    }

    [Fact]
    public void Preferred_view_keeps_a_favourite_vs_code_has_forgotten()
    {
        List<WorkspaceFavorite> favorites =
        [
            new() { Name = "delta", Uri = "file:///c%3A/code/delta/delta.code-workspace" }
        ];

        var row = Assert.Single(WorkspaceRows.Build([Alpha], favorites, showAll: false));

        Assert.Equal("delta", row.Name);
        Assert.Null(row.Id);
        Assert.Equal(WorkspaceKind.WorkspaceFile, row.Kind);
        Assert.Equal(@"c:\code\delta\delta.code-workspace", row.Location);
        Assert.True(row.Preferred);
    }

    [Fact]
    public void Preferred_view_falls_back_to_the_workspace_name_when_the_favourite_has_none()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "   ", Uri = "file:///c%3A/code/delta" }];

        var row = Assert.Single(WorkspaceRows.Build([], favorites, showAll: false));

        Assert.Equal("delta", row.Name);
    }

    [Fact]
    public void All_view_lists_every_workspace_most_recent_first()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "beta (mine)", Uri = Beta.Uri }];

        var rows = WorkspaceRows.Build([Alpha, Beta, Gamma], favorites, showAll: true);

        Assert.Equal(["alpha", "beta (mine)", "gamma"], rows.Select(row => row.Name));
        Assert.Equal([false, true, false], rows.Select(row => row.Preferred));
    }

    [Fact]
    public void All_view_still_shows_a_favourite_vs_code_has_forgotten_first()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "delta", Uri = "file:///c%3A/code/delta" }];

        var rows = WorkspaceRows.Build([Alpha], favorites, showAll: true);

        Assert.Equal(["delta", "alpha"], rows.Select(row => row.Name));
        Assert.True(rows[0].Preferred);
    }

    [Fact]
    public void Matches_favourites_to_workspaces_regardless_of_case()
    {
        List<WorkspaceFavorite> favorites = [new() { Name = "alpha", Uri = Alpha.Uri.ToUpperInvariant() }];

        var row = Assert.Single(WorkspaceRows.Build([Alpha], favorites, showAll: true));

        Assert.True(row.Preferred);
        Assert.Equal("a", row.Id);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj --filter FullyQualifiedName~WorkspaceRowsTests`
Expected: FAIL — `WorkspaceRows` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `VsCode.cs`, after `VsCodeLauncher`:

```csharp
/// <summary>One line of the Workspaces grid.</summary>
internal sealed class WorkspaceRow
{
    public required string Uri { get; init; }

    /// <summary>The storage id, or null for a favourite VS Code has no storage directory for.</summary>
    public string? Id { get; init; }

    public required WorkspaceKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public bool Preferred { get; init; }

    /// <summary>Filled in per refresh by probing the workspace's state database.</summary>
    public bool IsOpen { get; set; }
}

/// <summary>Decides which workspaces the grid shows and in which order.</summary>
internal static class WorkspaceRows
{
    public static List<WorkspaceRow> Build(
        IReadOnlyList<VsCodeWorkspace> discovered,
        IReadOnlyList<WorkspaceFavorite> favorites,
        bool showAll)
    {
        var workspaceByUri = new Dictionary<string, VsCodeWorkspace>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in discovered)
        {
            workspaceByUri[workspace.Uri] = workspace;
        }

        var favoriteByUri = new Dictionary<string, WorkspaceFavorite>(StringComparer.OrdinalIgnoreCase);
        foreach (var favorite in favorites)
        {
            favoriteByUri[favorite.Uri] = favorite;
        }

        var rows = new List<WorkspaceRow>();

        foreach (var favorite in favorites)
        {
            var known = workspaceByUri.GetValueOrDefault(favorite.Uri);

            // In the all-recent view a known favourite is listed below, in recency order; only the
            // ones VS Code has forgotten need listing here, so that nothing silently disappears.
            if (showAll && known is not null)
            {
                continue;
            }

            rows.Add(FromFavorite(favorite, known));
        }

        if (!showAll)
        {
            return rows;
        }

        foreach (var workspace in discovered)
        {
            var favorite = favoriteByUri.GetValueOrDefault(workspace.Uri);
            rows.Add(new WorkspaceRow
            {
                Uri = workspace.Uri,
                Id = workspace.Id,
                Kind = workspace.Kind,
                Name = Named(favorite, workspace.DisplayName),
                Location = workspace.Location,
                Preferred = favorite is not null
            });
        }

        return rows;
    }

    private static WorkspaceRow FromFavorite(WorkspaceFavorite favorite, VsCodeWorkspace? known)
    {
        if (known is not null)
        {
            return new WorkspaceRow
            {
                Uri = known.Uri,
                Id = known.Id,
                Kind = known.Kind,
                Name = Named(favorite, known.DisplayName),
                Location = known.Location,
                Preferred = true
            };
        }

        // VS Code has no storage directory for this one, so everything comes from the URI itself and
        // it can be launched but not probed.
        var (kind, name, location) = VsCodeUri.Describe(favorite.Uri);
        return new WorkspaceRow
        {
            Uri = favorite.Uri,
            Id = null,
            Kind = kind,
            Name = Named(favorite, name),
            Location = location,
            Preferred = true
        };
    }

    private static string Named(WorkspaceFavorite? favorite, string fallback)
    {
        return string.IsNullOrWhiteSpace(favorite?.Name) ? fallback : favorite.Name;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 38 tests.

- [ ] **Step 5: Commit**

```bash
git add VsCode.cs RestartApps.Tests
git commit -m "Build the rows shown on the Workspaces tab"
```

---

### Task 7: Tabbed layout

Puts the existing application UI onto an "Apps" tab and adds an empty "Workspaces" tab beside it. Behaviour must not change; this is purely the container.

**Files:**
- Modify: `RestartApps.csproj` (add `Krypton.Navigator`)
- Modify: `Program.cs` (constructor layout, new `BuildTabs`, `BuildAppsPage`, `BuildWorkspacesPage`)

**Interfaces:**
- Consumes: nothing.
- Produces: fields `_tabs` (`KryptonNavigator`) and `_workspacesPage` (`KryptonPage`) on `RestartAppsForm`, used by Task 8.

There are no unit tests here: the WinForms layer is verified by running the app, as the spec states.

- [ ] **Step 1: Add the package**

In `RestartApps.csproj`, add to the existing `PackageReference` item group:

```xml
    <PackageReference Include="Krypton.Navigator" Version="105.26.4.110" />
```

Run: `dotnet restore`
Expected: restore succeeds.

- [ ] **Step 2: Add the fields and the using**

In `Program.cs`, add `using Krypton.Navigator;` beside the existing `using Krypton.Toolkit;`, and add these fields to `RestartAppsForm` beside `_grid`:

```csharp
    private readonly KryptonNavigator _tabs = new();
    private readonly KryptonPage _workspacesPage = new();
```

- [ ] **Step 3: Rebuild the form layout**

In the `RestartAppsForm` constructor, replace the block that builds `layout` and adds rows 0 to 4 — everything from `var layout = new TableLayoutPanel` down to and including `layout.Controls.Add(BuildActionBar(), 0, 4);` — with:

```csharp
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        Controls.Add(layout);

        layout.Controls.Add(BuildHeader(), 0, 0);
        layout.Controls.Add(BuildTabs(), 0, 1);
        layout.Controls.Add(BuildActionBar(), 0, 2);
```

and change the log line that follows from `layout.Controls.Add(_txtLog, 0, 5);` to:

```csharp
        layout.Controls.Add(_txtLog, 0, 3);
```

- [ ] **Step 4: Add the tab builders**

In `Program.cs`, in the `chrome` region right after `BuildHeader()`, add:

```csharp
    private KryptonNavigator BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.NavigatorMode = NavigatorMode.BarTabGroup;
        _tabs.Button.CloseButtonDisplay = ButtonDisplay.Hide;
        _tabs.AllowPageDrag = false;
        _tabs.Pages.Add(BuildAppsPage());
        _tabs.Pages.Add(BuildWorkspacesPage());
        _tabs.SelectedIndex = 0;
        return _tabs;
    }

    private KryptonPage BuildAppsPage()
    {
        var page = new KryptonPage
        {
            Text = "Apps",
            TextTitle = string.Empty,
            TextDescription = string.Empty
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Padding = new Padding(4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildProfileBar(), 0, 0);

        _quickRun.Dock = DockStyle.Fill;
        _quickRun.BackColor = Color.Transparent;
        _quickRun.WrapContents = false;
        _quickRun.AutoScroll = true;
        _quickRun.Padding = new Padding(0, 2, 0, 2);
        layout.Controls.Add(_quickRun, 0, 1);

        BuildGrid();
        layout.Controls.Add(_grid, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private KryptonPage BuildWorkspacesPage()
    {
        _workspacesPage.Text = "Workspaces";
        _workspacesPage.TextTitle = string.Empty;
        _workspacesPage.TextDescription = string.Empty;
        return _workspacesPage;
    }
```

The `_quickRun` setup and the `BuildGrid()` call now live in `BuildAppsPage`; Step 3 already removed them from the constructor, so the constructor must not mention `_quickRun` or `_grid` any more.

If the compiler rejects `_tabs.Button.CloseButtonDisplay` or `_tabs.AllowPageDrag` on this Krypton.Navigator version, drop those two lines: `NavigatorMode.BarTabGroup` plus the two pages is all the feature needs.

- [ ] **Step 5: Build and run**

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

Run: `dotnet run`
Expected: the window opens with an Apps tab and a Workspaces tab. On the Apps tab, the profile bar, one-click buttons and the application grid look and behave exactly as before: ticking Kill and Restart works, Load and Save As on a profile work, and the log pane and action buttons are below the tabs. The Workspaces tab is empty. Close the window.

- [ ] **Step 6: Commit**

```bash
git add RestartApps.csproj Program.cs
git commit -m "Move the application list onto an Apps tab"
```

---

### Task 8: The Workspaces tab

Fills the tab: the toolbar, the grid, the open/closed probe on refresh, the preferred tick box and the Launch/Focus button.

**Files:**
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `VsCodeWorkspaces.Discover` / `IsRunning` / `IsOpen` / `DefaultStorageRoot` (Tasks 2-3), `VsCodeLauncher` (Task 4), `ConfigurationFile.Workspaces` and `VsCodeExecutable` (Task 5), `WorkspaceRows.Build` and `WorkspaceRow` (Task 6), and the existing `LogMessage` and `SaveConfiguration` on the form.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the fields**

In `Program.cs`, beside `_workspacesPage`:

```csharp
    private readonly KryptonDataGridView _workspaceGrid = new();
    private readonly KryptonCheckBox _showAllWorkspaces = new();
    private readonly KryptonLabel _workspaceCount = new();
    private readonly KryptonButton _btnRefreshWorkspaces = new();
    private readonly System.Windows.Forms.Timer _workspaceRefreshTimer = new() { Interval = 300 };
```

- [ ] **Step 2: Build the page contents**

Replace the body of `BuildWorkspacesPage` from Task 7 with:

```csharp
    private KryptonPage BuildWorkspacesPage()
    {
        _workspacesPage.Text = "Workspaces";
        _workspacesPage.TextTitle = string.Empty;
        _workspacesPage.TextDescription = string.Empty;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildWorkspaceBar(), 0, 0);

        BuildWorkspaceGrid();
        layout.Controls.Add(_workspaceGrid, 0, 1);

        _workspacesPage.Controls.Add(layout);
        return _workspacesPage;
    }

    private TableLayoutPanel BuildWorkspaceBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));

        _showAllWorkspaces.Text = "Show all recent";
        _showAllWorkspaces.Dock = DockStyle.Fill;
        _showAllWorkspaces.Margin = new Padding(0, 2, 6, 2);
        _showAllWorkspaces.CheckedChanged += (_, _) => RefreshWorkspaces();
        bar.Controls.Add(_showAllWorkspaces, 0, 0);

        _workspaceCount.Dock = DockStyle.Fill;
        _workspaceCount.Margin = new Padding(0, 2, 6, 2);
        bar.Controls.Add(_workspaceCount, 1, 0);

        _btnRefreshWorkspaces.Text = "Refresh";
        _btnRefreshWorkspaces.Dock = DockStyle.Fill;
        _btnRefreshWorkspaces.Margin = new Padding(0, 2, 0, 2);
        _btnRefreshWorkspaces.ToolTipValues.EnableToolTips = true;
        _btnRefreshWorkspaces.ToolTipValues.Description = "Re-read the workspace list and check which ones are open.";
        _btnRefreshWorkspaces.Click += (_, _) => RefreshWorkspaces();
        bar.Controls.Add(_btnRefreshWorkspaces, 2, 0);

        return bar;
    }

    private void BuildWorkspaceGrid()
    {
        _workspaceGrid.Dock = DockStyle.Fill;
        _workspaceGrid.AutoGenerateColumns = false;
        _workspaceGrid.AllowUserToAddRows = false;
        _workspaceGrid.AllowUserToDeleteRows = false;
        _workspaceGrid.AllowUserToResizeRows = false;
        _workspaceGrid.AllowUserToOrderColumns = false;
        _workspaceGrid.RowHeadersVisible = false;
        _workspaceGrid.MultiSelect = false;
        _workspaceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _workspaceGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        _workspaceGrid.Columns.Add(new KryptonDataGridViewCheckBoxColumn
        {
            Name = "Preferred",
            HeaderText = "Pref",
            Width = 46,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Keep this workspace on the preferred list.",
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            Name = "Workspace",
            HeaderText = "Workspace",
            Width = 150,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            Name = "Location",
            HeaderText = "Location",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "Status",
            Width = 56,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewButtonColumn
        {
            Name = "Action",
            HeaderText = string.Empty,
            Width = 76,
            UseColumnTextForButtonValue = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.CellContentClick += WorkspaceGrid_CellContentClick;
    }
```

- [ ] **Step 3: Add the refresh and the handlers**

Add a new region to `Program.cs`, before the `misc` region:

```csharp
    // ------------------------------------------------------------ workspaces

    /// <summary>Re-reads the workspace list and re-checks which of the shown rows are open.</summary>
    private void RefreshWorkspaces()
    {
        // A tick box left mid-edit would otherwise throw when the rows are replaced.
        _workspaceGrid.EndEdit();

        var discovered = VsCodeWorkspaces.Discover(VsCodeWorkspaces.DefaultStorageRoot);
        var rows = WorkspaceRows.Build(discovered, _config.Workspaces, _showAllWorkspaces.Checked);

        // With VS Code shut down nothing can be open, and the probe can be skipped entirely.
        var running = VsCodeWorkspaces.IsRunning();
        foreach (var row in rows)
        {
            row.IsOpen = running
                && row.Id is not null
                && VsCodeWorkspaces.IsOpen(VsCodeWorkspaces.DefaultStorageRoot, row.Id);
        }

        _workspaceGrid.Rows.Clear();
        foreach (var row in rows)
        {
            var index = _workspaceGrid.Rows.Add(
                row.Preferred,
                row.Name,
                row.Location,
                row.IsOpen ? "Open" : string.Empty,
                row.IsOpen ? "Focus" : "Launch");

            var gridRow = _workspaceGrid.Rows[index];
            gridRow.Tag = row;
            gridRow.Cells["Location"].ToolTipText = row.Uri;
        }

        _workspaceCount.Text = _showAllWorkspaces.Checked
            ? $"{rows.Count} recent"
            : $"{rows.Count} preferred";
    }

    private void WorkspaceGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _workspaceGrid.Rows[e.RowIndex].Tag is not WorkspaceRow row)
        {
            return;
        }

        switch (_workspaceGrid.Columns[e.ColumnIndex].Name)
        {
            case "Action":
                LaunchWorkspace(row);
                return;

            case "Preferred":
                TogglePreferred(row);
                return;
        }
    }

    /// <summary>Preferred is a stated preference, so unlike the Kill/Restart ticks it is saved at once.</summary>
    private void TogglePreferred(WorkspaceRow row)
    {
        var existing = _config.Workspaces
            .FirstOrDefault(workspace => string.Equals(workspace.Uri, row.Uri, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _config.Workspaces.Add(new WorkspaceFavorite { Name = row.Name, Uri = row.Uri });
            LogMessage($"Added {row.Name} to the preferred workspaces.");
        }
        else
        {
            _config.Workspaces.Remove(existing);
            LogMessage($"Removed {existing.Name} from the preferred workspaces.");
        }

        SaveConfiguration();
        RefreshWorkspaces();
    }

    private void LaunchWorkspace(WorkspaceRow row)
    {
        var executable = VsCodeLauncher.ResolveExecutable(_config.VsCodeExecutable);
        if (executable is null)
        {
            LogMessage("Could not find VS Code. Set \"VsCodeExecutable\" in the configuration file.");
            return;
        }

        try
        {
            Process.Start(VsCodeLauncher.CreateStartInfo(executable, VsCodeLauncher.BuildArguments(row.Uri, row.Kind)));
            LogMessage(row.IsOpen ? $"Focusing {row.Name}..." : $"Launching {row.Name}...");
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to launch {row.Name}: {ex.Message}");
        }
    }
```

- [ ] **Step 4: Wire the refresh triggers**

In the constructor, next to `_reloadTimer.Tick += ReloadTimer_Tick;`, add:

```csharp
        _workspaceRefreshTimer.Tick += (_, _) =>
        {
            _workspaceRefreshTimer.Stop();
            RefreshWorkspaces();
        };

        _tabs.SelectedPageChanged += (_, _) =>
        {
            if (_tabs.SelectedPage == _workspacesPage)
            {
                RefreshWorkspaces();
            }
        };

        // Coming back to the window is the moment the open/closed state is most likely stale.
        Activated += (_, _) =>
        {
            if (_tabs.SelectedPage == _workspacesPage)
            {
                _workspaceRefreshTimer.Stop();
                _workspaceRefreshTimer.Start();
            }
        };
```

In `FormClosed`, add `_workspaceRefreshTimer.Stop();` beside `_reloadTimer.Stop();`.

At the end of `ApplyConfiguration()`, after `RebuildProfileControls();`, add:

```csharp
        RefreshWorkspaces();
```

- [ ] **Step 5: Build and run through the checklist**

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

Run: `dotnet run`, then with at least two VS Code windows open (one of them this repository) check each of:

1. The Workspaces tab opens on an empty list reading `0 preferred` — `restartapps.json` has no `Workspaces` yet.
2. Tick `Show all recent`: the list fills, most recently used first, and the count reads `<n> recent`. The workspaces open in VS Code show `Open` and a `Focus` button; the rest show an empty status and a `Launch` button. Remote entries read `SSH: <host>`, `WSL: <distro>` or `Dev Container` under Location; hovering a Location cell shows the full URI.
3. Tick `Pref` on two entries. The log says they were added, `restartapps.json` gains a `Workspaces` array with both, and the file's own editor window (if open) reloads without complaint.
4. Untick `Show all recent`: only those two rows remain, and the count reads `2 preferred`.
5. Click `Launch` on a closed workspace: VS Code opens it. Click `Refresh`: the row flips to `Open` / `Focus`.
6. Click `Focus` on an open workspace: that VS Code window comes to the front and no second window appears.
7. Close one of those VS Code windows, click `Refresh`: the row goes back to `Launch`.
8. Untick `Pref` on one row: the log says it was removed and it disappears from `restartapps.json`.
9. Edit the `Name` of the remaining entry by hand in `restartapps.json` and save: the grid picks up the new name without restarting the app.
10. Switch to the Apps tab and run `Kill && Start Selected` with a harmless app ticked: it still behaves as before.

- [ ] **Step 6: Commit**

```bash
git add Program.cs
git commit -m "Add the Workspaces tab"
```

---

### Task 9: Documentation and a clean verification pass

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Document the tab**

In `README.md`, add this section after the Profiles section and before the build instructions:

```markdown
### Workspaces (.NET version only)

The **Workspaces** tab lists VS Code workspaces and folders, says which ones VS Code currently has
open, and launches them:

| Control | Effect |
| --- | --- |
| **Pref** | Keeps the workspace on the preferred list. Saved to `restartapps.json` straight away. |
| **Launch** / **Focus** | Opens the workspace in VS Code. A workspace that is already open is focused rather than opened twice. |
| **Show all recent** | Widens the list from the preferred entries to every workspace VS Code has opened, most recent first. |
| **Refresh** | Re-reads the list and re-checks which entries are open. |

The list is read from `%APPDATA%\Code\User\workspaceStorage`, so it covers local folders,
`.code-workspace` files, SSH remotes, WSL and dev containers alike. An entry counts as open when a
VS Code window still holds its workspace state database — window titles are not used, since
`window.title` is configurable and need not contain the path.

Preferred entries are stored in a `Workspaces` array in `restartapps.json`:

```jsonc
"Workspaces": [
  { "Name": "develop (linux)", "Uri": "vscode-remote://ssh-remote%2Bhost/home/cce/workspaces/develop/develop.code-workspace" }
]
```

`Name` is only a label: rename it to tell two same-named workspaces apart. Add
`"VsCodeExecutable": "..."` at the top level of the file if VS Code is not installed in one of the
usual places.
```

Also add to the Notes section at the end:

```markdown
- The Workspaces tab reads VS Code Stable's storage under `%APPDATA%\Code`. Insiders and other
  forks are not detected.
```

- [ ] **Step 2: Run the whole suite and build**

Run: `dotnet test RestartApps.Tests\RestartApps.Tests.csproj`
Expected: PASS, 38 tests, 0 failed.

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Confirm the working tree holds only intended changes**

Run: `git status --short` and `git diff --stat HEAD~8`
Expected: changes limited to `VsCode.cs`, `Program.cs`, `Configuration.cs`, `RestartApps.csproj`, `README.md`, `RestartApps.Tests/`, and the spec and plan documents. `restartapps.json` may have gained a `Workspaces` array from the manual checks — decide whether to keep those entries or revert the file, and say which in the commit.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Document the Workspaces tab"
```
