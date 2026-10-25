# Development guide

How the Developer Workspace Launcher code is organised, and how to build, run and test it. The
top-level [README](../../README.md) is the user guide; this folder is for whoever changes the code.
Keep it current: a change that alters the layout, the build or a behaviour described here updates
this folder in the same commit.

## Projects

| Project | What it is |
| --- | --- |
| `src/DevWorkspaceLauncher.Core` | UI-free shared library (`net10.0-windows`): configuration, app and workspace operations, Win32 helpers. |
| `src/DevWorkspaceLauncher.WinForms` | The WinForms app, themed with Krypton. |
| `tests/DevWorkspaceLauncher.Core.Tests` | xUnit tests for Core. |

`DevWorkspaceLauncher.slnx` at the root holds them all.

Dependency rule: the apps reference Core, never each other. Core references no UI framework - no
WinForms, no WinUI types, not even `System.Windows.Forms.Message`. Anything both apps need lives in
Core.

`RestartApps.ps1` is the original PowerShell prototype. It is kept for reference only and is not
maintained.

## Build, run, test

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build DevWorkspaceLauncher.slnx
dotnet test tests/DevWorkspaceLauncher.Core.Tests
dotnet run --project src/DevWorkspaceLauncher.WinForms
```

Publishing:

```powershell
dotnet publish src/DevWorkspaceLauncher.WinForms -c Release
```

Output: `src\DevWorkspaceLauncher.WinForms\bin\Release\net10.0-windows\publish`, a framework-dependent,
ReadyToRun win-x64 build. The publish settings live in the project file, so that command is complete.

## Naming rules

- Display name *Developer Workspace Launcher*; code name `DevWorkspaceLauncher`. Both, and the
  configuration file names, are defined once in `AppIdentity` (Core). Use those constants; never
  spell a name out where it ends up on disk.
- No spaces in any file or folder name - projects, solution, configuration, build and publish output.
  The spaced display name is for text people read only.

## Configuration

`devworkspacelauncher.example.json` at the root is an example. It is not copied to the build output.
Machine-specific files called `*.local.json` are git-ignored.
