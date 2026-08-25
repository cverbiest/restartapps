# RestartApps

A small Windows utility that stops a configured set of desktop applications and starts the ones
you select — handy after a VPN change, a docking-station reconnect, or when tray apps get stuck.

Two implementations are included:

- **`Program.cs` / `Configuration.cs` / `Native.cs`** — a .NET 10 WinForms app
  (`RestartApps.csproj`), themed with the
  [Krypton Toolkit](https://github.com/Krypton-Suite/Standard-Toolkit). This is the current version.
- **`RestartApps.ps1`** — the original PowerShell/WinForms prototype, kept for reference and for
  machines where you would rather not build anything.

Both read the same `restartapps.json` configuration file. The prototype understands only the
original fields and only the original bare-array layout.

## Screens and behaviour

The .NET window shows a grid with one row per configured application and a log pane underneath.
Two tick boxes per row control what happens, both starting from the configuration and both editable
in place:

| Column | Meaning |
| --- | --- |
| `Kill` | This app is stopped by **Kill && Start Selected**. Defaults from the `Kill` field. |
| `Restart` | This app is started, and is the one **Restart Selected** acts on. Defaults from the `Restart` field. |

Edits made in the grid apply to the current session only — they are not written back to
`restartapps.json`, so the file stays the source of the defaults. (A drag-and-drop addition does
save, and it writes the tick states as they stand at that moment.)

- **Kill && Start Selected** — stops every application ticked `Kill`, waits a second, then starts
  every application ticked `Restart`. Either half may be empty; the log says so and the other half
  still runs.
- **Restart Selected** — stops and restarts only the applications ticked `Restart`, regardless of
  their `Kill` tick (.NET version only).
- **Edit Config** — opens `restartapps.json` in whatever editor is associated with `.json`,
  falling back to Notepad if there is no association (.NET version only).

Processes are matched by executable name derived from the command line and terminated together with
their child processes. Failures to stop a process are ignored; failures to start one are logged.

### Profiles (.NET version only)

A profile is a named set of Kill/Restart ticks. The bar above the grid manages them, and every
profile also gets its own button on the "One click" row:

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
in `restartapps.json` and every profile operation saves the file immediately.

### Live configuration reload (.NET version only)

The configuration file is watched while the window is open. Saving it — from the **Edit Config**
editor or from anywhere else — reloads the list within about half a second and logs the reload.
Writes are debounced, so editors that save through a temporary file plus a rename trigger a single
reload, and a file still locked by the editor is retried a few times.

Because the file is the source of truth, a reload also resets the tick boxes to the `Kill` and
`Restart` flags it contains, discarding grid edits made since the last load.

### Adding applications by drag and drop (.NET version only)

Drop an executable or a shortcut anywhere on the window to append it to the configuration:

- `.lnk` shortcuts are resolved to their target and arguments; if the target cannot be read, the
  shortcut itself is used as the command line.
- The entry is named after the dropped file and added with both flags on (`"Kill": true`,
  `"Restart": true`).
- Entries whose name is already present are skipped.
- The current tick states are preserved, and `restartapps.json` is rewritten immediately.

### Stopping browser-hosted PWAs (.NET version only)

A PWA installed through Chrome/Edge/Brave **has no process of its own**. Chromium runs one browser
process per user-data-dir, and every profile and installed app is just another window inside it —
so killing by executable name takes down the entire browser and all its windows. The launcher named
in the shortcut (`chrome_proxy.exe`) exits immediately after handing off, so matching on that name
finds nothing at all.

What *is* unique is the window's AppUserModelID. Setting `StopMethod` to `Window` and giving a
`WindowAumid` pattern makes RestartApps close exactly those windows with `WM_CLOSE`, never touching
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

Windows are asked to close politely; RestartApps waits up to three seconds and logs any that
refuse. Nothing is force-killed by this method.

## Configuration

`restartapps.json` sits next to the executable (or in the current directory). The current layout is
an object with `Apps` and `Profiles`; a bare array of applications (the original format) is still
read and is upgraded to the new layout the next time the file is saved.

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

Comments and trailing commas are tolerated. The file shipped in this repository is an example —
edit it to match your own machine. Anything machine-specific you do not want to commit can go in a
`restartapps.local.json`, which is git-ignored.

## Build and run (.NET version)

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet run
```

To produce a standalone build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

`restartapps.json` is copied to the output directory as part of the build.

## Run (PowerShell version)

```powershell
powershell -ExecutionPolicy Bypass -File .\RestartApps.ps1
```

## Notes

- Windows only — both versions use WinForms and terminate processes by name.
- Killing applications by process name affects *all* instances of that executable, including ones
  you did not start from this tool.

## License

[MIT](LICENSE)
