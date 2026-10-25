# Developer Workspace Launcher: rename, shared core, WinUI 3 app and activities

Date: 2026-10-25. Status: approved to build (the user asked to start building after the
brainstorming, without a further review round).

## Intent

RestartApps is growing into a personal launcher for a developer's working day: VS Code workspaces,
desktop applications and, later, RDP sessions. It should fit the author's needs first, but be tidy
enough that others can pick it up.

This work does four things:

1. **Renames** the app to *Developer Workspace Launcher*.
2. **Splits** the code into a UI-free shared library used by two desktop front ends.
3. **Adds a WinUI 3 front end** next to the existing WinForms/Krypton one, with full feature parity.
   WinUI is a trial: Microsoft promotes it, and the author will keep whichever version they prefer.
   Neither app may depend on the other winning.
4. **Adds activities**: one button that starts (and stops) everything linked to a piece of work.

It also starts **development docs** that are kept current from now on.

## Goals

- Both apps run side by side, from the same configuration file, and behave the same.
- Everything that is not UI lives in the shared library and is unit tested there.
- The WinForms app keeps every current behaviour; the only visible changes are the new name and the
  activity buttons.
- The WinUI app has every feature of the WinForms app, laid out the Windows 11 way (layout "B" below).
- Activities replace the need for two separate kinds of one-click button over time.

## Non-goals

- RDP sessions. The activity model leaves room for them; nothing is built.
- An in-app editor for activities. Activities are written in the configuration file for now.
- MSIX packaging, the Store or winget. Both apps are unpackaged folders for now.
- Removing profiles or sessions. They keep working exactly as today, in both apps.
- Renaming the repository folder or the git remote.
- Changing `RestartApps.ps1`, the original PowerShell prototype. It stays as a historical reference.

## Anti-goals

- Losing the author's existing configuration, or splitting it into two files that drift apart.
- Any space in a file or folder name: projects, solution, config file, build or publish output.
  The spaced name *Developer Workspace Launcher* appears only as display text.
- Scattering the app name through persisted identifiers. It comes from one place (see Naming).

## Naming

| Thing | Name |
| --- | --- |
| Display name (window titles, README) | Developer Workspace Launcher |
| Code name (namespaces, projects, folders, files) | `DevWorkspaceLauncher` |
| Configuration file | `devworkspacelauncher.json` |
| Shared configuration folder | `%APPDATA%\DevWorkspaceLauncher\` |

The display name, code name and configuration file name are defined once, in a static class in the
shared library, and everything else reads them from there. A later rename is then a change to that
class plus a migration step like the one below.

## Repository layout

```
DevWorkspaceLauncher.slnx
src/
  DevWorkspaceLauncher.Core/          UI-free shared library (net10.0-windows)
  DevWorkspaceLauncher.WinForms/      today's Krypton app
  DevWorkspaceLauncher.WinUI/         the new WinUI 3 app
tests/
  DevWorkspaceLauncher.Core.Tests/    xUnit, today's tests plus new ones
docs/
  development/                        development docs (new, kept current)
  superpowers/                        specs and plans
devworkspacelauncher.example.json     example configuration (was restartapps.json)
RestartApps.ps1                       untouched prototype
```

The WinForms and WinUI apps reference Core only. They never reference each other.

## Shared library (Core)

Moves in as-is (namespace renamed): the configuration model and store, the VS Code workspace code,
window finding, shortcut resolution, command line parsing and the environment refresh.

Moves in by being extracted from the WinForms form, so both apps call the same code:

- **Stopping and starting apps**: kill by process name, close by window AUMID, start a command line,
  "Kill && Start Selected" and "Restart Selected" as operations over a list of apps.
- **Profiles and sessions**: load, save as, update, delete, and the rules they follow today
  (an app a profile does not list is cleared; a session only opens).
- **Workspace operations**: open, focus, close-with-strict-title-match, the open-state refresh.
- **Configuration file watching**: the debounced, retrying reload, raised as an event.
- **Drag-and-drop addition**: turning a dropped `.exe` or `.lnk` into an app entry.
- **Activities** (below).

Core does not reference WinForms or WinUI types. The environment refresh's "is this an environment
change" test takes the raw message number and `lParam` instead of a WinForms `Message`.
Logging goes through a callback or event that each app routes to its own log view.

## Configuration file location and migration

Today the file sits next to the executable. With two apps in two output folders that would split the
configuration in two, so the lookup becomes:

1. `devworkspacelauncher.json` next to the executable, if present: a portable override.
2. Otherwise `%APPDATA%\DevWorkspaceLauncher\devworkspacelauncher.json`: the shared default.
3. If neither exists, migrate: copy the first `restartapps.json` found next to the executable or in
   the current directory to location 2, log that it did so, and use it. The old file is not deleted.
4. If there is nothing to migrate, create location 2 from an empty configuration.

The example configuration is no longer copied to the build output, because a copy there would
shadow the shared file via rule 1.

**One-off step for the author's machine:** the live file today is
`bin\Release\net10.0-windows\publish\restartapps.json` in the repository root, which the new exes
will not look in. During implementation, copy it to
`%APPDATA%\DevWorkspaceLauncher\devworkspacelauncher.json` (after asking the author first).

## Activities

An activity is a named button that acts on a set of linked items. Anything an activity does not list
is left alone. This differs from a profile, which clears every app it does not mention.

```jsonc
"Activities": [
  {
    "Name": "Docking",
    "Apps": [
      { "Name": "RDP Plus", "Action": "Restart" },
      { "Name": "Teams",    "Action": "Restart" },
      { "Name": "Outlook",  "Action": "Start" }
    ],
    "Workspaces": [
      { "Uri": "vscode-remote://ssh-remote%2Bcce-dev03/home/cvb/develop/develop.code-workspace" }
    ]
  },
  {
    "Name": "Wind down",
    "Apps": [ { "Name": "Teams", "Action": "Stop" }, { "Name": "Outlook", "Action": "Stop" } ]
  }
]
```

| Item | Field | Actions (case-insensitive) | Default |
| --- | --- | --- | --- |
| App | `Name`: an entry in `Apps` | `Start`: only if not already running. `Stop`: by its `StopMethod`. `Restart`: stop, then start. | `Start` |
| Workspace | `Uri`: as in `Workspaces` | `Open`: focus if already open. `Close`: ask the window to close, with the same strict title rule as Close Ticked. | `Open` |

- **Running** means: for `StopMethod: Window`, a window matching `WindowAumid` exists; otherwise a
  process with the executable name from the command line exists.
- **Order of a run**: all app stops and workspace closes first; a one-second wait if anything was
  stopped; then app starts and workspace opens, in the order listed.
- `Stop`/`Restart` on an app whose `StopMethod` is `None` logs that it cannot be stopped; `Restart`
  then behaves as `Start`.
- Unknown app names, disabled apps and unknown URIs are logged and skipped; the rest of the run goes on.
- A workspace URI need not be in the `Workspaces` favourites list; it is opened by URI either way.
- Workspace `Close` is implemented and tested for completeness; the author does not plan to use it yet.
- RDP sessions are a future third list (`RdpSessions`) in the same shape. Not built now.
- The planning of a run (which steps, in which order, given what is running) is a pure function in
  Core so it can be unit tested without starting anything.

Both apps show activities as one-click buttons that act immediately with no confirmation, like the
current profile and session buttons. Activities are edited in the configuration file; there is no
in-app editor in this work. The existing live reload picks up edits.

## WinForms app

Unchanged in behaviour. Changes: renamed project, namespace and window title; calls Core instead of
in-form logic; a new "One click activities" row of buttons above the existing two rows.

## WinUI app (layout B)

- **Windows App SDK 2.5.1**, unpackaged (`WindowsPackageType` None), with the App SDK runtime bundled
  (self-contained) so the folder runs on machines without the runtime installed. .NET stays
  framework-dependent, as in the WinForms app.
- **Left navigation rail** (NavigationView), top to bottom:
  - an **Activities** group: one item per activity; invoking it runs the activity (it does not
    navigate);
  - the pages: **Workspaces**, **Apps**, **Activity log**;
  - a **Sessions** group and an **App profiles** group: one item per session/profile, invoking runs
    it, exactly as the one-click buttons do today;
  - **Settings** at the bottom: configuration file path, Edit Config, and the app version.
- **Workspaces page**: the workspace list with Pref (star), Close and Open ticks, open state and a
  Launch/Focus button per row; a command bar with Open ticked, Close ticked, Refresh, Show all recent,
  and a session menu holding Load, Save As, Update and Delete.
- **Apps page**: the app list with Kill and Restart ticks (Kill locked for `StopMethod: None`); a
  command bar with Kill && Start Selected, Restart Selected, Edit Config, and a profile menu holding
  Load, Save As, Update and Delete.
- **Feedback**: the latest result in an InfoBar banner on the current page; every line also goes to
  the **Activity log** page, which is the full log.
- **Drag and drop** of `.exe`/`.lnk` anywhere on the window, as today.
- **Live configuration reload**, refresh on window activation, and the environment refresh on
  `WM_SETTINGCHANGE` (via a window subclass on the main window's handle), all as today.
- Light and dark theme follow Windows; Mica backdrop.

Where WinForms and WinUI differ in look, WinUI follows WinUI conventions; where they differ in
behaviour, that is a bug.

## Development docs

`docs/development/` is created in this work and updated in every later change that affects it:

- **README.md**: what the projects are and how they depend on each other; how to build, run, test
  and publish each app; where configuration lives; the naming rules (one name constant, no spaces).
- **architecture.md**: what lives in Core and why; how an app wires Core into its UI; how activities
  are planned and run; where a new feature goes (Core first, then both apps).

The top-level README stays the user guide, updated for the new name, the configuration location,
activities and the second app.

## Builder's calls

These were not discussed with the author and are left to the builder's judgement, within the above:

- The exact WinUI controls for the grids (ListView with columns, or a community DataGrid), the
  icons, and the spacing.
- How Core exposes logging and progress (event, callback or `IProgress`).
- Whether the WinUI app uses MVVM tooling (CommunityToolkit.Mvvm) or plain code-behind.
- The solution format (`.slnx` preferred, `.sln` acceptable).

## Calls made without the author

Made during brainstorming, flagged here so the author can check them:

- Shared configuration in `%APPDATA%\DevWorkspaceLauncher\`, with migration from `restartapps.json`
  and a portable next-to-exe override.
- Code name `DevWorkspaceLauncher`; repo folder and remote stay `restartapps`.
- The WinForms app also gets activity buttons, to keep the comparison fair.
- Activity app actions are Start/Stop/Restart, workspace actions Open/Close, with the defaults above.
- WinUI app bundles the Windows App SDK runtime (self-contained) for easy sharing.
