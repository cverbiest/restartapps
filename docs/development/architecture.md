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
| `VsCodeUri`, `VsCodeWorkspaces`, `VsCodeLauncher`, `VsCodeWindows`, `WorkspaceRows` | VS Code workspace discovery, open-state probe, launching, window matching, grid rows. |
| `WindowFinder`, `ShortcutResolver`, `CommandLineParser`, `UserEnvironment` | Win32 helpers. |

## Conventions

- **Logging.** Core never owns a log view. Operations that report progress take an
  `Action<string> log` and call it with complete, human-readable lines; each app routes it to its own
  log (adding the timestamp). Message texts are part of the behaviour - both apps show the same ones.
- **Async.** Operations that wait (closing windows, the pause between stop and start) are `async Task`.
  The apps await them on the UI thread and disable their run buttons meanwhile.
- **Saving.** Core changes the in-memory `ConfigurationFile`; the app decides when to call
  `AppConfigurationStore.Save` (and so when its configuration watcher should ignore the resulting write).
- **Session-only state.** Kill/Restart and Close/Open ticks are never saved, except as part of a
  profile or session. Core types carry them; the apps keep them across refreshes.
