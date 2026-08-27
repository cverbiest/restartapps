# VS Code workspace launcher

A Workspaces tab in Restart Apps that lists preferred VS Code workspaces and folders, shows
which ones a VS Code window currently has open, and launches or focuses them.

## Problem

Three to five VS Code workspaces get opened over and over: some local folders, some local
`.code-workspace` files, some over SSH, some in a dev container. Finding them means opening
VS Code first and hunting through *File > Open Recent*, and nothing says which are already open.

## Goals

- A short list of preferred workspaces, always visible, launched with one click.
- An honest open/closed indicator per entry.
- An option to widen the list to every recently used workspace, and to tick any of them into
  the preferred list.

## Non-goals

- Closing or killing VS Code windows.
- Workspaces as part of profiles or of Kill && Start Selected.
- VS Code Insiders, VSCodium or other forks.
- Adding a workspace by dropping a folder on the window.

## Discovery

`%APPDATA%\Code\User\workspaceStorage\<id>\workspace.json` holds one line per workspace VS Code
has ever opened:

```json
{ "folder": "file:///c%3A/cce/workspaces/restartapps" }
```

```json
{ "workspace": "vscode-remote://ssh-remote%2Bcce-dev03_cvb/home/cce/cvb/workspaces/develop/develop.code-workspace" }
```

The directory name is VS Code's workspace id. The directory's last-write time is when the
workspace was last used, so ordering by it descending gives the recent list. On the development
machine this is 157 entries and the four most recent are exactly the four open windows.

Two alternatives were rejected:

- `history.recentlyOpenedPathsList` in `state.vscdb` is the list VS Code itself renders, but the
  key is absent on the development machine (checked with the write-ahead log replayed), and
  reading it would add `Microsoft.Data.Sqlite` plus lock and WAL handling.
- `profileAssociations.workspaces` in `storage.json` knows 145 workspaces but carries no
  timestamps, so it cannot be ordered by recency.

Each `workspace.json` becomes a record:

| Field | Meaning |
| --- | --- |
| `Id` | The storage directory name. Used for the open probe. |
| `Uri` | The URI exactly as stored, which is what gets passed back to VS Code. |
| `Kind` | `Folder`, or `WorkspaceFile` when the path ends in `.code-workspace`. |
| `DisplayName` | Last path segment, with `.code-workspace` stripped. |
| `Location` | Decoded local path, or a remote label (see below). |
| `LastUsed` | The directory's last-write time. |

`Location` for a `vscode-remote://` URI comes from its authority: `ssh-remote+<host>` reads as
`SSH: <host>`, `wsl+<distro>` as `WSL: <distro>`, `dev-container+<hex>` as `Dev Container`, and
anything else falls back to the raw authority. For `file:` URIs it is the decoded Windows path.

Duplicate URIs collapse to the most recently used entry. A `workspace.json` that is missing,
unreadable or malformed is skipped; discovery never throws.

## Open detection

A VS Code window holds an exclusive-capable lock on its `workspaceStorage\<id>\state.vscdb` for as
long as the window lives. Opening that file with `FileShare.None` therefore answers the question:

- Sharing violation - a window has this workspace open.
- Success - it does not.
- File missing or any other error - reported as not open.

Verified against four open windows (local folder, local workspace file, SSH remote, dev container)
and two closed ones. Window titles were rejected as the signal: the machine's `window.title` is
`${dirty}${rootNameShort}${separator}${remoteName}${separator}${activeEditorShort}${separator}${profileName}`,
which yields titles such as `develop linux` that no path can be derived from.

Cost control: when no `Code` process is running every row is reported closed without touching the
disk, and only rows currently displayed are probed.

The probe runs when the Workspaces tab is selected, when the form is activated (debounced through a
timer, as the configuration watcher already does), and on an explicit Refresh button.

## Configuration

A third top-level property beside `Apps` and `Profiles`:

```jsonc
{
  "Apps": [ ... ],
  "Profiles": [ ... ],
  "Workspaces": [
    {
      "Name": "develop (linux)",
      "Uri": "vscode-remote://ssh-remote%2Bcce-dev03_cvb/home/cce/cvb/workspaces/develop/develop.code-workspace"
    }
  ],
  "VsCodeExecutable": "C:\\Program Files\\Microsoft VS Code\\Code.exe"
}
```

- `Workspaces` is optional, so existing configuration files load unchanged; `Save` always writes it,
  as it already does for `Profiles`.
- `Name` is seeded from `DisplayName` when the entry is ticked Preferred, and can be edited by hand
  in the file. The existing file watcher picks the edit up.
- `VsCodeExecutable` is optional and overrides executable discovery.
- Entries match discovered workspaces by `Uri`, compared case-insensitively. A preferred entry whose
  storage directory has been pruned still lists and still launches; it shows no open/closed state,
  because without an id there is nothing to probe.

Ticking or unticking Preferred writes `restartapps.json` immediately. This differs from the Kill and
Restart ticks, which stay session-only, and follows the precedent set by drag-and-drop addition: a
preference the user states explicitly is meant to outlive the session.

## User interface

The existing single-column layout gains a `KryptonNavigator` (package `Krypton.Navigator`, same
suite and version as the referenced `Krypton.Toolkit`, so the theming matches) holding two pages.
The action bar and the log pane stay below it, shared by both pages. The window keeps its size.

- **Apps** - the profile bar, the one-click row and the application grid, unchanged.
- **Workspaces** - a toolbar row (`Show all recent` tick box, `Refresh` button, a count label) above
  a grid.

| Column | Contents |
| --- | --- |
| `Pref` | Tick box. Ticking adds the entry to `Workspaces` and saves; unticking removes it and saves. |
| `Workspace` | `Name` for a preferred entry, otherwise `DisplayName`. |
| `Location` | The local path or the remote label. Fills the remaining width. |
| `Status` | `Open`, or empty. |
| Button | `Focus` when open, `Launch` when not. |

The default view is the preferred entries in configuration order. `Show all recent` swaps in every
discovered workspace, most recently used first, with `Pref` reflecting membership. The tick box
state is session-only; the tab opens on the preferred list every time.

## Launching

`Code.exe --folder-uri "<uri>"`, or `--file-uri` when `Kind` is `WorkspaceFile`. VS Code answers a
request for a workspace it already has open by focusing that window rather than opening a second
one, which is why the same command serves both captions.

The executable is resolved in order: `VsCodeExecutable` from the configuration, then
`%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe`, then `%ProgramFiles%\Microsoft VS Code\Code.exe`,
then `code.cmd` on `PATH`. Failure to resolve, and failure to start, are logged like every other
action; neither is fatal.

## Structure

- `VsCode.cs` (new) - discovery, URI decoding, label derivation, the open probe and executable
  resolution. The filesystem root and the probe sit behind a seam so tests can point them at a
  temporary directory.
- `Configuration.cs` - `WorkspaceFavorite`, the `Workspaces` list, `VsCodeExecutable`.
- `Program.cs` - the navigator, the Workspaces page and its handlers.
- `RestartApps.Tests` (new xUnit project) - URI decoding, remote-label derivation, `DisplayName`
  derivation, deduplication, discovery over a temporary directory tree, the open probe against a
  locked and an unlocked file, and configuration round-tripping with and without `Workspaces`.
  The WinForms layer is not tested.
- `README.md` - a Workspaces section describing the tab, the detection method and its limits.

## Risks

- The lock probe reads an implementation detail of VS Code's storage. If a future version stops
  holding the file, every row reports closed; launching still works, and the tests pin the probe's
  behaviour against a genuinely locked file rather than against VS Code.
- Discovery covers workspaces VS Code has opened at least once. One never opened on this machine
  cannot be listed, only added by hand to `restartapps.json`.
