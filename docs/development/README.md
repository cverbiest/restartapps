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
| `src/DevWorkspaceLauncher.WinUI` | The WinUI 3 app (Windows App SDK 2.5.1), unpackaged. A trial next to the WinForms app; both stay current. |
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
dotnet run --project src/DevWorkspaceLauncher.WinUI
```

The WinUI app needs no Visual Studio workload or templates: the `Microsoft.WindowsAppSDK` package
brings the XAML compiler, and the plain .NET SDK builds it.

Publishing:

```powershell
dotnet publish src/DevWorkspaceLauncher.WinForms -c Release
```

Output: `src\DevWorkspaceLauncher.WinForms\bin\Release\net10.0-windows\publish`, a framework-dependent,
ReadyToRun win-x64 build. The publish settings live in the project file, so that command is complete.

```powershell
dotnet publish src/DevWorkspaceLauncher.WinUI -c Release
```

Output: `src\DevWorkspaceLauncher.WinUI\bin\Release\net10.0-windows10.0.26100.0\publish`. Unpackaged
(`WindowsPackageType` None) with the Windows App SDK runtime carried in the folder
(`WindowsAppSDKSelfContained`), so it runs where that runtime is not installed; .NET itself is
framework-dependent, as for the WinForms app. The project sets no `Platforms`, so solution and
project builds share one output folder (`bin\<config>\<tfm>`); the runtime identifier pins x64.
The publish folder is about 150 MB, most of it the Windows App SDK runtime and its resources
(`RestartAgent.exe` in it belongs to the Windows App SDK).
The project copies the app's own `DevWorkspaceLauncher.WinUI.pri` into the publish folder after
publishing (target `CopyAppPriToPublishDir`): an unpackaged publish leaves it out, and without it
the compiled XAML cannot load, so the published app dies at startup with 0xC000027B. Always start
the *published* exe once after changing the build, not just `dotnet run`.

## Naming rules

- Display name *Developer Workspace Launcher*; code name `DevWorkspaceLauncher`. Both, and the
  configuration file names, are defined once in `AppIdentity` (Core). Use those constants; never
  spell a name out where it ends up on disk.
- No spaces in any file or folder name - projects, solution, configuration, build and publish output.
  The spaced display name is for text people read only.

## Configuration

`ConfigurationLocator` picks the file, in this order:

1. `devworkspacelauncher.json` next to the executable - a portable override, and the way to run a
   development build against a test configuration without touching your real one.
2. `%APPDATA%\DevWorkspaceLauncher\devworkspacelauncher.json` - the shared default, so both apps
   read and write the same file.
3. If neither exists: the first `restartapps.json` (pre-rename name) next to the executable or in the
   current directory is copied to location 2 and kept. Otherwise an empty configuration is written
   there. `AppConfigurationStore.EnsureExists()` does this at startup and returns a log line.

`devworkspacelauncher.example.json` at the root is an example. It is not copied to the build output.
Machine-specific files called `*.local.json` are git-ignored.
