# SSH hosts - first slice

## Intent

An SSH connection is one more thing the user launches, like an app, a VS Code workspace or an RDP
session. The user wants SSH hosts as launch items: pinned to the start page and the tray, placed in
activities, started with one click. A tab that lists the hosts and lets them be edited is the start;
choosing the SSH client (WezTerm first) comes later.

## Decided by the user

- An SSH host is a launch item like the others (2026-10-29).
- The first slice is a tab listing the hosts, where they can be edited (2026-10-29).
- `~/.ssh/config` is the one place the hosts live. The tab reads it and writes it back; the
  launcher's own configuration file does not hold a second copy of the hosts (2026-10-29, agreed to
  the playback).
- Every `Host` entry is listed, including git-only aliases such as `github.com-cw`; pinning is the
  filter (2026-10-29, agreed to the playback).
- The tab edits `HostName`, `User` and `IdentityFile`; anything else is edited in the file itself
  (2026-10-29, agreed to the playback).
- Starting a host with WezTerm, and options for that, come later (2026-10-29).

## Goals

- Read `%USERPROFILE%\.ssh\config` into a list of SSH hosts.
- An "SSH hosts" tab with the same layout, quick search, multi-select, sorting and pin column as the
  other grids (see the project memory "Consistent UX across tabs"). Columns: pin, alias, host name,
  user, identity file.
- Add, edit, delete and connect from the tab; double-click a row to connect, as the other grids run
  their row.
- SSH hosts in the All items tab, as favorites on the start page and in the tray (with the
  favorites' order), and as a fourth kind of item in an activity ("connect").
- Connect: `ssh <alias>` in a new Windows Terminal tab/window (`wt.exe`), or in a new console window
  when Windows Terminal is not installed. Using the alias means everything in the config entry
  applies, including what the tab does not edit.

## Non-goals for this slice

WezTerm and other clients, per-host client options, `Match` blocks, following `Include`, the system
`ssh_config`, editing keywords other than the three above, creating keys, `known_hosts`, port
forwarding, the command line, the WinForms app.

## Anti-goals

- Losing or reformatting anything in `.ssh/config` the user did not change in the tab: comments,
  blank lines, indentation, keyword case, order, other keywords, `Match` and `Include` lines. A save
  changes only the lines of the entries that were edited, added or deleted.
- Overwriting changes made to the file outside the launcher (by hand, by VS Code Remote-SSH).
- A password anywhere: SSH logs in with keys or prompts in the terminal.

## Design

### Reading the file

The file is kept as its list of lines. A `Host` line starts an entry, which runs until the next
`Host` or `Match` line. Lines before the first `Host` and every `Match` block are kept as they are and
not listed.

- A `Host` line with several patterns gives one item, named by its first pattern; the others are
  shown read-only (an "also" note) and kept on save.
- A pattern with `*`, `?` or `!` cannot be connected to: such an entry (`Host *`) is not listed.
- Keywords are matched case-insensitively; both `Keyword value` and `Keyword=value` are read.
- `Include` lines are kept but not followed.

### Writing the file

- Before every save, re-read the file. If it changed since it was read, do not write: reload the tab
  and log that the file changed outside the launcher, so the user redoes the edit.
- Edit: change the value on the existing keyword line, keeping its indentation and spelling; add a
  missing keyword as a new line after the `Host` line, indented like the entry's other lines (four
  spaces when there are none); clearing a field removes its line.
- Add: append a new entry at the end of the file, after a blank line.
- Delete: remove the entry's lines, from its `Host` line up to the next entry, keeping the blank
  line that separates entries.
- Keep the file's line endings and encoding (no BOM added). Write to a temporary file in the same
  folder and replace the original. The first save in a session keeps a copy as
  `config.<yyyyMMdd-HHmmss>.bak`, as the configuration file migration does.
- If the file does not exist, the tab is empty and the first add creates it (and `.ssh`).
- The alias must be one word without wildcards and not already used.

### What the launcher stores

The launcher's configuration file stores only launcher settings, keyed by alias:
`"SshHosts": [ { "Alias": "cce-dev03_cvb", "Favorite": true, "FavoriteOrder": 3 } ]`, written only
for hosts that have a setting. An activity names a host in `"SshHosts": [ { "Name": "..." } ]`,
like `RdpSessions`. Renaming an alias in the tab updates both; deleting a host removes its
settings and its place in activities. A setting or activity item whose alias is no longer in the
file is kept (the user may have renamed it by hand), shown as missing, and skipped with a log line
when an activity runs.

### Refreshing

The tab, the start page and the tray re-read the file when the launcher window is activated and when
the tab is opened, so edits made elsewhere show up.

## WezTerm (added 2026-10-29, asked for by the user)

WezTerm is the default client when it is installed; Windows Terminal stays a choice in Settings
(`"SshClient": "WezTerm" | "WindowsTerminal"` in the configuration; absent means automatic).

The user's WezTerm configuration makes an `SSH:<alias>` domain for every host in `~/.ssh/config`
(`wezterm.default_ssh_domains()`), and its F6 SSH menu and clipboard-image paste work through those
domains. So Connect opens a tab in that domain, not a tab running `ssh`:

- WezTerm running: `wezterm cli spawn --domain-name SSH:<alias>`, with `WEZTERM_UNIX_SOCKET` set to
  the newest running `wezterm-gui`'s `~/.local/share/wezterm/gui-sock-<pid>`, then its window is
  brought to the front. Found by trying: `wezterm connect --new-tab` and `wezterm start --new-tab
  --domain` started a second WezTerm, and the CLI's own socket discovery picked a dead socket.
- WezTerm not running, or the spawn failed: `wezterm-gui connect SSH:<alias>` (a new window).

## Calls made by the builder (not the user)

- A new entry goes at the end of the file, but before a trailing run of wildcard-only `Host` blocks
  (`Host *`) and `Match` blocks: ssh takes the first value it finds, so defaults after the new entry
  must not override it.
- A keyword that appears more than once in an entry (usually `IdentityFile`): the tab shows and edits
  the first; the others are kept and shown as "also". Clearing the field removes only the first line.
- Comment lines directly above a `Host` line belong to that entry: they move and go with it, and the
  blank lines between entries stay.
- Connect opens a new tab in the most recent Windows Terminal window (`wt -w 0 nt`), one call per
  host, like RDP sessions. Without Windows Terminal, a new console window per host.
- An entry whose first pattern has no wildcard is listed, even when a later pattern has one.
- Renaming an alias does not change `ProxyJump` or other references to it in the file.
- The tray reads the file again when its menu opens.
- A missing keyword is added after the entry's last setting line, not right after the `Host` line.

## Left to the builder

Names of types and files; how the editor dialog looks (follow the RDP session editor); how missing
hosts are shown; the order of the tab's columns beyond the pin column first; tests.
