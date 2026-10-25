# Architecture

## Core holds the behaviour, the apps hold the UI

Everything the launcher *does* lives in `DevWorkspaceLauncher.Core`. The two apps arrange controls,
collect ticks and choices from the person using them, and call Core. If the WinForms and WinUI apps
ever behave differently, that is a bug in one of them, and usually a sign that logic has leaked into
a front end.

A new feature therefore goes into Core first, with tests, and then into both apps.

| Core type | Responsibility |
| --- | --- |
| `AppIdentity` | The app's names and configuration file names. |
| `ConfigurationLocator`, `AppConfigurationStore` | Which configuration file to use; loading, migrating, saving. |
| `AppConfiguration`, `Profile`, `WorkspaceFavorite`, ... (`Configuration.cs`) | The configuration file's model. |
| `AppController` | Is an app running; stop it (by process or by window AUMID); start it; Kill && Start / Restart Selected. |
| `ProfileOperations` | App profiles: find, apply (unlisted apps are cleared), capture, upsert. |
| `DroppedApps` | Turning dropped `.exe`/`.lnk` files into app entries. |
| `ConfigFileOpener` | Edit Config: the `.json` association, falling back to Notepad. |
| `WorkspaceController` | Workspace rows with open state; open (batched, 400 ms apart), launch/focus, close (strict single title match), preferred toggle. |
| `SessionOperations` | Sessions: find, apply to the Open ticks, resolve names, upsert. |
| `ConfigurationWatcher` | Debounced, retrying reload of the configuration file, raised on the UI thread. Ignores content equal to `LastKnownJson`. |
| `VsCodeUri`, `VsCodeWorkspaces`, `VsCodeLauncher`, `VsCodeWindows`, `WorkspaceRows` | VS Code workspace discovery, open-state probe, launching, window matching, grid rows. |
| `WindowFinder`, `ShortcutResolver`, `CommandLineParser`, `UserEnvironment` | Win32 helpers. |

## Conventions

- **Logging.** Core never owns a log view. Operations that report progress take an
  `Action<string> log` and call it with complete, human-readable lines; each app routes it to its own
  log (adding the timestamp). Message texts are part of the behaviour - both apps show the same ones.
- **Async.** Operations that wait (closing windows, the pause between stop and start) are `async Task`.
  The apps await them on the UI thread and disable their run buttons meanwhile.
- **Saving.** Core changes the in-memory `ConfigurationFile`; the app decides when to call
  `AppConfigurationStore.Save`, and then sets its `ConfigurationWatcher.LastKnownJson` to the JSON
  written, so its own save does not come back as a reload. Another app (the other front end) saving
  the same file does reload - that is how the two stay in step when both are open.
- **Session-only state.** Kill/Restart and Close/Open ticks are never saved, except as part of a
  profile or session. Core types carry them; the apps keep them across refreshes.

## Activities

An activity run is split in two so the rules can be tested without starting anything:

- `ActivityPlanner.Plan(activity, config, isRunning)` is pure. It resolves each item (apps by name,
  case-insensitive; workspaces by URI, with name and window title from the preferred list when
  present), applies the rules - Start only when not running, StopMethod None cannot be stopped,
  unknown or disabled apps skipped - and returns ordered `ActivityStep`s plus `Notes` to log.
  Order: StopApp, CloseWorkspace, Wait (only if something was stopped), StartApp, OpenWorkspace.
- `ActivityRunner.RunAsync` plans with `AppController.IsRunning`, logs the notes and carries the
  steps out through `AppController` and `WorkspaceController`. Workspace opens go in one
  `OpenAsync` batch so VS Code gets its pause between windows.

Enums in the configuration (`StopMethod`, `AppAction`, `WorkspaceAction`) are read with
`CaseInsensitiveEnumConverter<T>`, whose error names the valid values.

RDP sessions are expected to become a third item list on `Activity` (`RdpSessions`) with its own step
kinds; nothing for them exists yet.
