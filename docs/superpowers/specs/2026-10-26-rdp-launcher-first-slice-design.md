# RDP launcher - first slice

The full feature plan (RDP+ parity, phases 0-6) lives in the Claude Docs doc
"RDP Launcher - Feature Plan (RDP+ parity)". This document covers only the first working version.

## Intent

Replace Remote Desktop Plus for daily use: start an RDP session to a named server at a known window
size, logged in automatically, with one click or one command line. RDP+ is unmaintained since 2018.

## Decided by the user

- First slice: the engine, the core command-line switches and a minimal RDP Sessions tab (2026-10-13).
- Session definitions live in the same configuration file as the apps. They only *reference*
  credentials; the configuration file never holds a password (2026-10-13).
- No import from RDP+, no reuse of RDP+ credentials (2026-10-13).
- mstsc stays the client, but none of its saved state is trusted: every launch writes an explicit
  `.rdp` file (2026-10-13).
- The password comes from a **generic credential in Windows Credential Manager** that the session
  names. At launch the app reads it and writes a short-lived `TERMSRV/<host>` credential for mstsc,
  removed again afterwards (2026-10-26).
- The tab is built in the **WinUI app only** for now; WinForms follows once the design settles
  (2026-10-26). This is a deliberate, temporary exception to "both apps at parity".

## Goals

- `RdpSession` entries in the configuration file: name, host (`host[:port]`), credential reference,
  optional username/domain override, size preset, display overrides, `/admin`, extra raw `.rdp`
  options, optional base `.rdp` file.
- Size presets, with two built in: `fit68` (68% of the monitor's working area, zoom 150%) and `full`
  (full screen). A preset of the same name in the file replaces the built-in one.
- Display modes: full screen, max (working area), fit (percent, or pixel margin when > 100), absolute
  width/height; target monitor; zoom (`desktopscalefactor`).
- Command line: `DevWorkspaceLauncher.WinUI.exe rdp <switches>` runs headless and exits with an
  RDP+-compatible exit code.
- WinUI: an "RDP sessions" page (list, Connect, Add, Edit, Delete) and the sessions on the rail as
  one-click items.

## Non-goals for this slice

Registry / `RDP` env var / `Default.rdpc` / Group Policy layers, command files, favorites, URI handler,
`/register`, `/title`, `/icon`, `/ontop`, multiple hosts, load balancing, redirection switches other
than via `/o`, RemoteApp, gateway switch, GUI/kiosk modes, `/allowed`, logging to file or event log,
tray, AD scan, activities containing RDP sessions, the WinForms tab.

## Anti-goals

- A password in the configuration file, on the command line or in the generated `.rdp` file.
- Touching a `TERMSRV/*` credential the app did not create.

## Design

### Option resolution ("last wins")

built-in preset → preset named by the session → the session's own fields → command-line switches.
All parse into one `RdpOptions` model. The other plan layers slot in later in front of the session.

### Command line

| Switch | Meaning |
| --- | --- |
| `/session:name` | Start from a saved session |
| `/v:host[:port]` | Target |
| `/cred:name` | Credential Manager target name to log in with |
| `/u:user`, `/domain:domain` | Username / domain (override the credential's username) |
| `/size:name` | Size preset |
| `/f`, `/max`, `/fit[:n]`, `/w:px /h:px` | Display mode |
| `/mon:n` | Monitor, 1-based; past the last falls back to 1 |
| `/zoom:percent` | `desktopscalefactor` |
| `/admin`, `/console` | mstsc `/admin` |
| `/o:"key:type:value,..."` | Raw `.rdp` options; native settings win; `\,` escapes a comma |
| `/title:"text"` | Window title; `%s` computer, `%u` user, local `%VARS%` expanded; kept while mstsc runs |
| `/drives[:spec]` | Redirect drives: all, or letters / fixed, removable, network, cdrom, dynamic; `-` excludes |
| `/i:name` | RDP+ spelling of `/cred:name` |
| `/wait` | Wait for mstsc to exit |
| first argument ending in `.rdp` | Base connection file |

Exit codes: 0 ok, 1 general error, 2 `.rdp` file not found, 30 credential or session not found,
87 invalid parameters.

### Launch

1. Resolve options; compute the window from the monitor's working area (physical pixels, the
   launcher is PerMonitorV2) minus the window frame.
2. Write `%TEMP%\<guid>.rdp`: base file lines, then model settings, then `/o` pairs that do not clash.
   Always written: `full address`, `screen mode id`, `desktopwidth`/`desktopheight`, `winposstr`,
   `smart sizing:i:0`, `prompt for credentials:i:0`, `promptcredentialonce:i:1`, `username`
   (`domain\user` when a domain is set). Never the password.
3. If the session names a credential: read it; write `TERMSRV/<host>` (generic, persisted for the
   logon session only) with its username and password.
4. Start `mstsc.exe "<file>" [/admin]`. Delete the `.rdp` file after 10 s.
5. Remove the `TERMSRV/<host>` credential when that mstsc process exits.

## Import from Remote Desktop Plus (added 2026-10-26, asked for by the user)

Sessions and options only, never RDP+ credential profiles. RDP+ favorites are encrypted;
a local, git-ignored export script decrypts them with RDP+'s own routine from `rdp.exe` and writes a
`name | secure=... | command line` listing (git-ignored as `rdpplus.favorites.txt`). The RDP sessions
page's "Import RDP+" reads that listing. `/i:profile` becomes the session's Credential; a favorite whose
name is already a session is skipped; switches not supported are logged per favorite. The import then
lists the credential names not yet in Credential Manager.

What the user's 35 favorites use, which set the priorities that followed: `/v` 35, `/title` 34, `/i` 32,
`/fit` 24, `/w /h` 11, `/drives` 7, `/o` (gateway) 4, `/admin` 2. Nothing else.

## Calls made by the builder (not the user)

- `authentication level` is **not** forced to 0 (the plan suggested it): that would hide
  server-certificate warnings. It can still be set per session via `/o`.
- If a `TERMSRV/<host>` credential already exists that this app did not write, it is left alone and
  the launch goes ahead without autologin; mstsc uses the existing entry. A log line says so.
- The temporary credential is removed on mstsc exit rather than "after mstsc has read it": mstsc reads
  it again on auto-reconnect. It is persisted for the logon session only, so it cannot outlive a logoff
  if the launcher is closed first.
- `/cred:name` replaces RDP+'s `/p` and `/pe`, which carry the password itself.
- CLI lives in the WinUI executable behind an `rdp` verb, to keep one portable exe.
- The command-line process stays (without a window) until mstsc exits whenever it wrote a credential or
  keeps a title; otherwise it exits after the temporary file is deleted.
- The import reads the decrypted listing rather than decrypting inside the launcher: it is a one-time
  move, and it keeps RDP+'s key out of the launcher.
