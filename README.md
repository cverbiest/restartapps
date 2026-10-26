# Developer Workspace Launcher

A Windows launcher for a developer's working day: one click starts the apps and opens the VS Code
workspaces that belong to a piece of work, and the tray apps that get stuck after a VPN change or a
docking-station reconnect can be stopped and started again. (It started life as *RestartApps*.)

Two desktop apps are included, side by side. They do the same things, read and write the same
configuration file, and can run at the same time:

- **`src/DevWorkspaceLauncher.WinUI`** — a WinUI 3 app (Windows App SDK) in the Windows 11 style:
  a navigation rail on the left with your activities, the pages, your sessions and app profiles.
- **`src/DevWorkspaceLauncher.WinForms`** — a WinForms app themed with the
  [Krypton Toolkit](https://github.com/Krypton-Suite/Standard-Toolkit): tabs, with rows of one-click
  buttons above them.

Both are built on **`src/DevWorkspaceLauncher.Core`**, which holds everything they do.
**`RestartApps.ps1`** is the original PowerShell/WinForms prototype, kept for reference only; it reads
only the original bare-array configuration layout.

## Screens and behaviour

Both apps show the same lists and buttons; where they differ is layout. In the WinUI app, activities,
sessions and app profiles are items in the navigation rail, the latest result shows in a banner, and
the full log has its own **Activity log** page; **Settings** shows where the configuration file is.
In the WinForms app they are rows of one-click buttons above the tabs, with the log at the bottom.

The **Apps** list has one row per configured application.
Two tick boxes per row control what happens, both starting from the configuration and both editable
in place:

| Column | Meaning |
| --- | --- |
| `Kill` | This app is stopped by **Kill && Start Selected**. Defaults from the `Kill` field. |
| `Restart` | This app is started, and is the one **Restart Selected** acts on. Defaults from the `Restart` field. |

Edits made in the grid apply to the current session only — they are not written back to
the configuration file, so the file stays the source of the defaults. (A drag-and-drop addition does
save, and it writes the tick states as they stand at that moment.)

- **Kill && Start Selected** — stops every application ticked `Kill`, waits a second, then starts
  every application ticked `Restart`. Either half may be empty; the log says so and the other half
  still runs.
- **Restart Selected** — stops and restarts only the applications ticked `Restart`, regardless of
  their `Kill` tick.
- **Edit Config** — opens the configuration file in whatever editor is associated with `.json`,
  falling back to Notepad if there is no association.

Processes are matched by executable name derived from the command line and terminated together with
their child processes. Failures to stop a process are ignored; failures to start one are logged.

### Profiles

A profile is a named set of Kill/Restart ticks. The bar above the grid manages them, and every
profile also gets its own button on the "One click apps" row. That row sits above the tabs, so the
profiles stay one click away whichever tab is showing:

| Control | Effect |
| --- | --- |
| **Load** | Applies the selected profile's ticks to the grid. Nothing is started or stopped. |
| **Save As...** | Stores the current ticks under a new name (offers to overwrite an existing one). |
| **Update** | Overwrites the selected profile with the current ticks. |
| **Delete** | Removes the selected profile, after confirming. |
| **One-click button** | Loads that profile *and* immediately runs Kill && Start Selected. |

The one-click buttons act at once — there is no confirmation step — so treat them as the "do the
thing" shortcut they are meant to be.

An application the profile has never heard of is cleared (neither killed nor started) rather than
left at whatever it happened to be, so a profile always describes the whole run. Profiles are stored
in the configuration file and every profile operation saves the file immediately.

### Workspaces

The window opens on **Workspaces**, the first of its two tabs; **Apps** is the second and is
everything described above. **Workspaces** lists VS Code workspaces and folders, says which ones VS
Code currently has open, and launches them:

| Control | Effect |
| --- | --- |
| **Pref** | Keeps the workspace on the preferred list. Saved to the configuration file straight away. |
| **Close** | Include this window in **Close Selected**. Only available while the workspace is open. |
| **Open** | Include this workspace in **Open Selected**, and in a saved session. |
| **Launch** / **Focus** | Opens the workspace in VS Code. One that is already open is focused rather than opened a second time. |
| **Close Selected** | Asks the ticked windows to close. |
| **Open Selected** | Opens every workspace ticked Open. |
| **Show all recent** | Widens the list from the preferred entries to every workspace VS Code has opened, most recent first. |
| **Refresh** | Re-reads the list and re-checks which entries are open. |

Like the Kill and Restart ticks, Close and Open are session-only and are never written to the file.

The list is read from `%APPDATA%\Code\User\workspaceStorage`, so it covers local folders,
`.code-workspace` files, SSH remotes, WSL and dev containers alike, and it is ordered by when each
was last opened. An entry counts as open when a VS Code window still holds its workspace state
database. Window titles are deliberately not used for this: `window.title` is configurable and need
not contain the path at all.

The list also refreshes when you switch to the tab and when the window is activated, so it is
usually current without pressing Refresh.

#### Closing a window

Closing asks the window to close, exactly as **Stopping browser-hosted PWAs** above does — a posted
`WM_CLOSE`, never a kill — so VS Code's save prompts and hot exit still run and unsaved work is
never thrown away. A window that has not gone after three seconds is left alone and logged; it is
usually sitting on a "do you want to save" dialog.

Which window belongs to which workspace can only be read from the window *title*, because VS Code
puts every window in one process under one AppUserModelID. So the rule is deliberately strict:
**a window is closed only when exactly one open window matches.** If two match — two workspaces both
called `develop`, say — nothing is closed and the log names the candidates. Pin the right one with a
wildcard title pattern on the workspace entry:

```jsonc
{ "Name": "develop (linux)", "Uri": "vscode-remote://ssh-remote%2Bhost/...", "WindowTitle": "develop linux*" }
```

#### Sessions

A **session** is a named set of workspaces to open together — the workspace counterpart of a
profile, and deliberately open-only: a session never closes anything.

| Control | Effect |
| --- | --- |
| **Load** | Ticks Open for the session's workspaces and clears it everywhere else. Nothing is opened. |
| **Save As...** | Stores the workspaces ticked Open under a new name. |
| **Update** | Overwrites the selected session with the workspaces ticked Open. |
| **Delete** | Removes the selected session, after confirming. |
| **One-click button** | Opens that session's workspaces straight away, including any the current filter hides. |

The session buttons live on the "One click sessions" row above the tabs, next to the profile
buttons, so both sets stay reachable from either tab.

### Activities

An **activity** is one button for a piece of work: it starts, stops or restarts the apps linked to it
and opens the VS Code workspaces linked to it, all in one click. Anything an activity does not list
is left alone — unlike a profile, which clears every app it does not mention. That makes it possible
to have buttons that only stop a few apps, next to buttons that start a whole working setup.

| Item | Actions |
| --- | --- |
| App | **Start** — only if it is not already running (for a PWA: if no window with its `WindowAumid` is open). **Stop** — by its `StopMethod`. **Restart** — stop, then start. |
| Workspace | **Open** — VS Code focuses it if it is already open. **Close** — asks the window to close, with the same one-matching-window rule as **Close Selected**. |

A run does all the stops and closes first, waits a second if anything was stopped, then does the
starts and opens, each in the order listed. Items that cannot be done — an app name that is not in
`Apps`, a disabled app, stopping an app whose `StopMethod` is `None` — are logged and skipped, and the
rest of the run goes ahead. Activity buttons act at once, without confirmation.

Activities are written in the configuration file (see below); there is no editor in the app yet.

### Live configuration reload

The configuration file is watched while the window is open. Saving it — from the **Edit Config**
editor or from anywhere else — reloads the list within about half a second and logs the reload.
Writes are debounced, so editors that save through a temporary file plus a rename trigger a single
reload, and a file still locked by the editor is retried a few times.

Because the file is the source of truth, a reload also resets the tick boxes to the `Kill` and
`Restart` flags it contains, discarding grid edits made since the last load.

### Adding applications by drag and drop

Drop an executable or a shortcut anywhere on the window to append it to the configuration:

- `.lnk` shortcuts are resolved to their target and arguments; if the target cannot be read, the
  shortcut itself is used as the command line.
- The entry is named after the dropped file and added with both flags on (`"Kill": true`,
  `"Restart": true`).
- Entries whose name is already present are skipped.
- The current tick states are preserved, and the configuration file is rewritten immediately.

### Stopping browser-hosted PWAs

A PWA installed through Chrome/Edge/Brave **has no process of its own**. Chromium runs one browser
process per user-data-dir, and every profile and installed app is just another window inside it —
so killing by executable name takes down the entire browser and all its windows. The launcher named
in the shortcut (`chrome_proxy.exe`) exits immediately after handing off, so matching on that name
finds nothing at all.

What *is* unique is the window's AppUserModelID. Setting `StopMethod` to `Window` and giving a
`WindowAumid` pattern makes the launcher close exactly those windows with `WM_CLOSE`, never touching
the browser process:

```json
{
  "Name": "Outlook",
  "Commandline": "\"C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\chrome_proxy.exe\" --profile-directory=\"Profile 5\" --app-id=faolnafnngnfdaknnbpnkhgohbobgegn",
  "StopMethod": "Window",
  "WindowAumid": "*_crx_faolngohbobgegn*",
  "Kill": true,
  "Restart": true
}
```

To find the value for your own app, note that Chromium builds the ID from a **truncated** app-id —
the first 5 and last 10 characters — so it will not equal the `--app-id` in the command line. A
wildcard around the truncated fragment, as above, is the practical way to write it. The
`*_crx_*` fragment is what distinguishes an installed app window from an ordinary browser window.

Windows are asked to close politely; the launcher waits up to three seconds and logs any that
refuse. Nothing is force-killed by this method.

## Configuration

The configuration is `%APPDATA%\DevWorkspaceLauncher\devworkspacelauncher.json`, shared by every
copy of the app. A `devworkspacelauncher.json` next to the executable takes precedence, for a
portable copy. On first start, if neither exists, a `restartapps.json` from before the rename (next
to the executable or in the current directory) is copied to the shared location and left in place;
otherwise an empty configuration is created. The log says which happened. If an empty one was
created while your old `restartapps.json` lives somewhere else (next to an older copy of the app,
say), close both apps and copy that file over the shared one. Migration never overwrites an existing
shared file. The current layout is an object with `Apps`, `Profiles` and `Workspaces`; a bare array of applications (the original
format) is still read and is upgraded to the new layout the next time the file is saved.

```json
{
  "Apps": [
    {
      "Name": "RDP Plus",
      "Commandline": "\"%ProgramFiles(x86)%\\Remote Desktop Plus\\rdp.exe\" /tray",
      "Kill": true,
      "Restart": true,
      "Disabled": false,
      "StopMethod": "Process"
    },
    {
      "Name": "Teams",
      "Commandline": "%LOCALAPPDATA%\\Microsoft\\WindowsApps\\ms-teams.exe",
      "Kill": false,
      "Restart": false,
      "Disabled": false,
      "StopMethod": "None"
    }
  ],
  "Profiles": [
    {
      "Name": "Docking",
      "Entries": [
        { "Name": "RDP Plus", "Kill": true, "Restart": true },
        { "Name": "Teams", "Kill": false, "Restart": false }
      ]
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `Name` | Label shown in the grid. |
| `Commandline` | Command used to start the app. Quote the executable if the path contains spaces; arguments follow it. Environment variables are expanded. |
| `Kill` | Whether `Kill` is ticked when the window opens. Optional — **omitting it means `true`**, so configurations written before this field existed keep stopping everything. Forced to `false` when `StopMethod` is `None`. |
| `Restart` | Whether `Restart` is ticked when the window opens. |
| `Disabled` | `true` hides the entry from the grid and excludes it from every action. The entry stays in the file, so this is the way to shelve an app without deleting it. Optional, defaults to `false`. |
| `StopMethod` | How the app is stopped: `Process` (default — kill by executable name), `Window` (close windows matching `WindowAumid`), or `None` (**never stopped**; its Kill box is shown locked). Case-insensitive. |
| `WindowAumid` | Only for `StopMethod: "Window"`: the AppUserModelID of the windows to close, with `*` and `?` wildcards. See the PWA section above. |

Every field except `Name` and `Commandline` is optional. The `Profiles` list may be omitted or empty.

| Profile field | Meaning |
| --- | --- |
| `Name` | Shown in the drop-down and on the profile's one-click button. |
| `Entries` | Per-application `Name` / `Kill` / `Restart`. Applications not listed are cleared when the profile loads. |

The preferred VS Code workspaces live in `Workspaces`, and are written by the Pref tick box rather
than by hand:

```json
  "Workspaces": [
    { "Name": "develop (linux)", "Uri": "vscode-remote://ssh-remote%2Bcce-dev03/home/cvb/develop/develop.code-workspace" },
    { "Name": "restartapps", "Uri": "file:///c%3A/cce/workspaces/restartapps" }
  ]
```

| Workspace field | Meaning |
| --- | --- |
| `Name` | Label shown in the grid, and nothing more. Rename it to tell two same-named workspaces apart — the app picks the change up on the next reload. |
| `Uri` | The workspace URI as VS Code stores it. This is what identifies the entry and what launches it. |
| `WindowTitle` | Optional. Which window belongs to this workspace, with `*` and `?` wildcards. Only needed when the name alone matches more than one open window; see *Closing a window* above. |

Sessions live in `WorkspaceProfiles`, each a name and the URIs it opens:

```json
  "WorkspaceProfiles": [
    { "Name": "Linux dev", "Workspaces": [ "vscode-remote://ssh-remote%2Bcce-dev03/home/cvb/develop/develop.code-workspace" ] }
  ]
```

Activities live in `Activities`:

```jsonc
  "Activities": [
    {
      "Name": "Docking",
      "Apps": [
        { "Name": "RDP Plus", "Action": "Restart" },
        { "Name": "Outlook" }
      ],
      "Workspaces": [
        { "Uri": "vscode-remote://ssh-remote%2Bcce-dev03/home/cvb/develop/develop.code-workspace" }
      ]
    },
    { "Name": "Wind down", "Apps": [ { "Name": "Teams", "Action": "Stop" } ] }
  ]
```

| Activity field | Meaning |
| --- | --- |
| `Name` | Shown on the activity's button. |
| `Apps` | Optional. Each entry names an app from `Apps` (case-insensitive) and an `Action`: `Start` (default), `Stop` or `Restart`. |
| `Workspaces` | Optional. Each entry has a workspace `Uri` and an `Action`: `Open` (default) or `Close`. The URI need not be on the preferred list; its `Name` and `WindowTitle` are used when it is. |

Actions are case-insensitive.

Add `"VsCodeExecutable": "..."` at the top level if VS Code is not in one of the usual places;
otherwise `%LOCALAPPDATA%\Programs\Microsoft VS Code`, `%ProgramFiles%\Microsoft VS Code` and
`code.cmd` on `PATH` are tried in that order.

Comments and trailing commas are tolerated. `devworkspacelauncher.example.json` in this repository
is an example to copy from.

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows. Nothing else: the
WinUI app's toolchain comes from its NuGet package.

```powershell
dotnet run --project src/DevWorkspaceLauncher.WinUI
dotnet run --project src/DevWorkspaceLauncher.WinForms
```

To produce a standalone build:

```powershell
dotnet publish src/DevWorkspaceLauncher.WinForms -c Release
```

The publish options live in `DevWorkspaceLauncher.WinForms.csproj`, not on the command line, so that
plain command is the whole story. It produces a framework-dependent, ReadyToRun win-x64 build in
`src\DevWorkspaceLauncher.WinForms\bin\Release\net10.0-windows\publish`. ReadyToRun precompiles the IL ahead of time - the Krypton
assemblies as well as the app - which is what a cold start would otherwise spend its time JITting.
It roughly doubles the size of the published folder, to about 23 MB, in exchange for that startup.

Being framework-dependent, the published app needs the .NET 10 desktop runtime on the machine that
runs it. Set `<SelfContained>true</SelfContained>` in the project file if you would rather carry the
runtime along.

The WinUI app publishes the same way:

```powershell
dotnet publish src/DevWorkspaceLauncher.WinUI -c Release
```

It lands in `src\DevWorkspaceLauncher.WinUI\bin\Release\net10.0-windows10.0.26100.0\publish`. It is
unpackaged — run the `.exe` from that folder, no installer — and carries the Windows App SDK runtime
in the folder, so it also needs only the .NET 10 desktop runtime.

## Run (PowerShell version)

```powershell
powershell -ExecutionPolicy Bypass -File .\RestartApps.ps1
```

## Notes

- Windows only — both apps use Windows UI frameworks and terminate processes by name.
- Killing applications by process name affects *all* instances of that executable, including ones
  you did not start from this tool.
- The Workspaces tab reads VS Code Stable's storage under `%APPDATA%\Code`. Insiders and other
  forks are not detected.
- A dev container built from a WSL folder keeps both storage entries live, so that folder can show
  as open under `WSL:` as well as under `Dev Container`.

## Tests

```powershell
dotnet test tests/DevWorkspaceLauncher.Core.Tests
```

See [docs/development](docs/development/README.md) for how the projects fit together.

Covers the workspace URI decoding, the storage scan, the open probe, executable resolution, the
grid's row rules, configuration round-tripping and location/migration, profiles, sessions, dropped
files, the configuration watcher and activity planning. The two UI layers are not unit tested.

## License

[MIT](LICENSE)
