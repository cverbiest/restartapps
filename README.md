# RestartApps

A small Windows utility that stops a configured set of desktop applications and starts the ones
you select — handy after a VPN change, a docking-station reconnect, or when tray apps get stuck.

Two implementations are included:

- **`Program.cs`** — a .NET 10 WinForms app (`RestartApps.csproj`), themed with the
  [Krypton Toolkit](https://github.com/Krypton-Suite/Standard-Toolkit). This is the current version.
- **`RestartApps.ps1`** — the original PowerShell/WinForms prototype, kept for reference and for
  machines where you would rather not build anything.

Both read the same `restartapps.json` configuration file.

## Screens and behaviour

The window lists every application from the configuration, pre-checked according to its `Restart`
flag, with a log pane underneath.

- **Kill All && Start Selected** — stops *every* application in the configuration, waits a second,
  then starts only the checked ones.
- **Restart Selected** — stops and restarts only the checked applications (.NET version only).

Processes are matched by executable name derived from the command line and terminated together with
their child processes. Failures to stop a process are ignored; failures to start one are logged.

## Configuration

`restartapps.json` sits next to the executable (or in the current directory) and holds a JSON array:

```json
[
  {
    "Name": "RDP Plus",
    "Commandline": "\"%ProgramFiles(x86)%\\Remote Desktop Plus\\rdp.exe\" /tray",
    "Restart": true
  },
  {
    "Name": "Teams",
    "Commandline": "%LOCALAPPDATA%\\Microsoft\\WindowsApps\\ms-teams.exe",
    "Restart": false
  }
]
```

| Field | Meaning |
| --- | --- |
| `Name` | Label shown in the list. |
| `Commandline` | Command used to start the app. Quote the executable if the path contains spaces; arguments follow it. Environment variables are expanded. |
| `Restart` | Whether the entry is checked when the window opens. |

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
