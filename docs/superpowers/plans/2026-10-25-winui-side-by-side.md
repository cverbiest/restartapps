# Developer Workspace Launcher (shared core, WinUI app, activities) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename RestartApps to Developer Workspace Launcher, move all non-UI logic into a shared
library, add activities, and add a WinUI 3 app with full parity next to the WinForms app.

**Architecture:** `DevWorkspaceLauncher.Core` (UI-free, `net10.0-windows`) holds configuration,
app/workspace operations, profiles, sessions, activities, the config watcher and the Win32 helpers.
`DevWorkspaceLauncher.WinForms` (today's Krypton app) and `DevWorkspaceLauncher.WinUI` (Windows App
SDK 2.5.1, unpackaged) are thin front ends over Core. Core logs through an `Action<string>` callback.

**Tech Stack:** .NET 10, WinForms + Krypton 105.26.4.110, Windows App SDK 2.5.1 (WinUI 3), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-25-winui-side-by-side-design.md`

## Global Constraints

- Display name `Developer Workspace Launcher`; code name `DevWorkspaceLauncher`; config file
  `devworkspacelauncher.json`; shared folder `%APPDATA%\DevWorkspaceLauncher\`. All four come from
  `AppIdentity` in Core; no other file spells them out.
- No space in any file or folder name (projects, solution, config, build or publish output).
- Core references neither WinForms nor WinUI. The two apps reference Core only, never each other.
- WinForms behaviour stays exactly as today, including every log message text, except: new name, new
  config location, the activities row.
- `RestartApps.ps1` is not modified.
- Every task that changes how the code is laid out, built or behaves updates `docs/development/`
  in the same commit.
- Build must be warning-free for new code (`dotnet build -warnaserror` is not required, but do not add warnings).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **The author's existing configuration** (`restartapps.json` beside the old exe) must survive: the
  first run migrates it, never overwrites an existing shared file. Pinned in Task 2.
- **Both apps open at once:** a save by one reloads the other, and neither reloads on its own write.
  Pinned in Task 4 (watcher test with two watchers on one file).
- **An activity naming an app or workspace that was renamed or removed** logs and skips that item; the
  rest of the run still happens. Pinned in Task 5.
- **Activity JSON written by hand:** lower-case actions (`"restart"`), missing `Action`, missing
  `Apps` or `Workspaces` lists all load with the defaults. Pinned in Task 5.
- **Start of an app that is already running** (including a PWA identified by window AUMID) must not
  start a second copy. Planner pinned in Task 5 via the `isRunning` predicate; `AppController.IsRunning`
  is checked by hand in Task 10.

---

### Task 1: Restructure into Core, WinForms and tests under the new name

Mechanical move; no behaviour change except names.

**Files:**
- Create: `DevWorkspaceLauncher.slnx` (projects: Core, WinForms, Core.Tests; WinUI added in Task 7)
- Create: `src/DevWorkspaceLauncher.Core/DevWorkspaceLauncher.Core.csproj` (`net10.0-windows`, Nullable, ImplicitUsings, `InternalsVisibleTo` DevWorkspaceLauncher.Core.Tests)
- Create: `src/DevWorkspaceLauncher.Core/AppIdentity.cs`
- Move (`git mv`): `Configuration.cs`, `VsCode.cs`, `Native.cs` → `src/DevWorkspaceLauncher.Core/`
- Move: `Program.cs` → `src/DevWorkspaceLauncher.WinForms/Program.cs`; `RestartApps.csproj` → `src/DevWorkspaceLauncher.WinForms/DevWorkspaceLauncher.WinForms.csproj`
- Move: `RestartApps.Tests/*` → `tests/DevWorkspaceLauncher.Core.Tests/` (csproj renamed `DevWorkspaceLauncher.Core.Tests.csproj`)
- Move: `restartapps.json` → `devworkspacelauncher.example.json` (repo root)
- Modify: `.gitignore` (`restartapps.local.json` → `devworkspacelauncher.local.json`; keep `*.local.json`)
- Create: `docs/development/README.md`

**Interfaces:**
- Produces: namespace `DevWorkspaceLauncher` for every type. Every type used across assemblies becomes `public` (configuration model, `AppConfigurationStore`, `VsCodeUri`, `VsCodeWorkspaces`, `VsCodeLauncher`, `WorkspaceRow`, `VsCodeWindows`, `WorkspaceRows`, `CommandLineParser`, `WindowFinder`, `ShortcutResolver`, `UserEnvironment`, enums).
- Produces: `public static class AppIdentity { const string DisplayName = "Developer Workspace Launcher"; const string CodeName = "DevWorkspaceLauncher"; const string ConfigFileName = "devworkspacelauncher.json"; const string LegacyConfigFileName = "restartapps.json"; }`
- Produces: `UserEnvironment.IsEnvironmentChange(uint message, IntPtr lParam)` replacing the `Message` overload (Core must not see WinForms).

- [ ] **Step 1:** `git mv` the files listed above; rename namespaces `RestartApps` → `DevWorkspaceLauncher` (also in tests).
- [ ] **Step 2:** Core csproj gets no WinForms reference; WinForms csproj keeps all current properties (publish settings, Krypton packages), drops `DefaultItemExcludes` and the `restartapps.json` copy item, adds `<ProjectReference>` to Core, `<AssemblyName>DevWorkspaceLauncher.WinForms</AssemblyName>`. Test csproj drops `UseWindowsForms`, references Core.
- [ ] **Step 3:** Change `IsEnvironmentChange` to `(uint message, IntPtr lParam)`; the form's `WndProc` passes `(uint)m.Msg, m.LParam`. Form `Text` and header label use `AppIdentity.DisplayName`. `FindConfigFile` uses `AppIdentity.ConfigFileName` for now (Task 2 replaces it).
- [ ] **Step 4:** Write `docs/development/README.md`: project list and dependency rule, `dotnet build DevWorkspaceLauncher.slnx`, `dotnet test tests/DevWorkspaceLauncher.Core.Tests`, `dotnet run --project src/DevWorkspaceLauncher.WinForms`, publish command and output path, naming rules from Global Constraints. Update the top-level README's build/test commands and paths.
- [ ] **Step 5:** Run `dotnet build DevWorkspaceLauncher.slnx` → succeeds; `dotnet test tests/DevWorkspaceLauncher.Core.Tests` → all 54 pass. `git status` shows renames (not delete+add) for the moved files.
- [ ] **Step 6:** Commit "Rename to DevWorkspaceLauncher and split into Core, WinForms and tests".

### Task 2: Shared configuration location with migration

**Files:**
- Create: `src/DevWorkspaceLauncher.Core/ConfigurationLocator.cs`
- Modify: `src/DevWorkspaceLauncher.Core/Configuration.cs` (`AppConfigurationStore.FilePath`, `Load`)
- Test: `tests/DevWorkspaceLauncher.Core.Tests/ConfigurationLocatorTests.cs`

**Interfaces:**
- Produces: `public sealed record ConfigurationLocation(string Path, string? MigrateFrom);`
- Produces: `public static ConfigurationLocation ConfigurationLocator.Resolve(string executableDirectory, string currentDirectory, string appDataDirectory, Func<string, bool> fileExists)` — pure.
- Produces: `public static string ConfigurationLocator.SharedDirectory(string appDataDirectory)` → `Path.Combine(appDataDirectory, AppIdentity.CodeName)`.
- Produces: `AppConfigurationStore.FilePath` (unchanged name) and `public static string? AppConfigurationStore.EnsureExists()` — performs migration/creation, returns a log line describing what it did or null.

- [ ] **Step 1: Write failing tests** in `ConfigurationLocatorTests` (use fake dirs `C:\exe`, `C:\cwd`, `C:\appdata` and a `HashSet` for `fileExists`):
  - `Portable_file_next_to_the_exe_wins`: exists `C:\exe\devworkspacelauncher.json` and the shared one → Path `C:\exe\devworkspacelauncher.json`, MigrateFrom null.
  - `Shared_file_is_the_default`: only `C:\appdata\DevWorkspaceLauncher\devworkspacelauncher.json` → that path, MigrateFrom null.
  - `Legacy_file_next_to_the_exe_is_migrated`: only `C:\exe\restartapps.json` → Path shared, MigrateFrom `C:\exe\restartapps.json`.
  - `Legacy_file_in_the_current_directory_is_migrated`: only `C:\cwd\restartapps.json` → MigrateFrom that.
  - `Existing_shared_file_is_never_replaced_by_a_legacy_one`: shared and `C:\exe\restartapps.json` exist → MigrateFrom null.
  - `Nothing_anywhere_means_a_new_shared_file`: → Path shared, MigrateFrom null.
  - `EnsureExists_copies_and_keeps_the_legacy_file` (real temp dirs via an internal overload `EnsureExists(ConfigurationLocation)`): after the call the shared file has the legacy content and the legacy file still exists; a second call returns null.
  - `EnsureExists_creates_an_empty_configuration`: new file parses to a `ConfigurationFile` with no apps.
- [ ] **Step 2:** Run `dotnet test tests/DevWorkspaceLauncher.Core.Tests --filter ConfigurationLocatorTests` → fails to compile.
- [ ] **Step 3:** Implement. Order exactly as the spec: portable → shared → legacy exe dir → legacy cwd → new shared. `EnsureExists` creates the directory, copies (never overwrites), or writes `ToJson(new ConfigurationFile())`. `FilePath` = `Resolve(AppContext.BaseDirectory, Environment.CurrentDirectory, Environment.GetFolderPath(SpecialFolder.ApplicationData), File.Exists).Path`. Log text: `Migrated configuration from {from} to {to}.` / `Created a new configuration at {to}.`
- [ ] **Step 4:** WinForms `Main` calls `EnsureExists()` before `Load()` and the form logs the returned line (pass it to the form constructor). Run all tests → pass.
- [ ] **Step 5:** Update `docs/development/README.md` (configuration lookup order) and the top-level README Configuration section. Commit "Keep the configuration in a shared folder, migrating restartapps.json".

### Task 3: Move app operations, profiles, drop handling and Edit Config into Core

**Files:**
- Create: `src/DevWorkspaceLauncher.Core/AppController.cs`, `ProfileOperations.cs`, `DroppedApps.cs`, `ConfigFileOpener.cs`
- Modify: `src/DevWorkspaceLauncher.WinForms/Program.cs` (delete the moved methods; call Core)
- Test: `tests/DevWorkspaceLauncher.Core.Tests/ProfileOperationsTests.cs`, `DroppedAppsTests.cs`

**Interfaces** (all `public static`; `log` is `Action<string>`; messages are today's texts verbatim):
- `AppController.IsRunning(AppConfiguration app) : bool` — Window method: `WindowFinder.FindByAumid(aumid).Count > 0` (false if no aumid); otherwise `Process.GetProcessesByName(CommandLineParser.GetProcessName(...)).Length > 0` (dispose processes).
- `AppController.StopAsync(AppConfiguration app, Action<string> log) : Task` — today's `StopAppAsync` + `CloseWindowsAsync` + `StopProcesses`.
- `AppController.Start(AppConfiguration app, Action<string> log) : void` — logs `Starting {Name}...`, runs today's `StartCommand`, logs failure.
- `AppController.KillAndStartAsync(IReadOnlyList<AppConfiguration> visibleApps, bool stopOnlySelected, Action<string> log) : Task` — body of today's `RestartAppsAsync` between the busy guard and `SetBusy(false)`, including every message and the 1 s delay.
- `ProfileOperations.Find(IEnumerable<Profile>, string? name) : Profile?` (case-insensitive)
- `ProfileOperations.Apply(Profile, IEnumerable<AppConfiguration> apps) : int` (returns cleared count; Kill only if `CanStop`)
- `ProfileOperations.Capture(IEnumerable<AppConfiguration> apps) : List<ProfileEntry>`
- `ProfileOperations.Upsert(List<Profile>, string name, List<ProfileEntry>) : Profile`
- `DroppedApps.Add(ConfigurationFile, IEnumerable<string> paths, Action<string> log) : List<AppConfiguration>` — appends to `config.Apps`, returns the added ones (caller saves and refreshes).
- `ConfigFileOpener.Open(string path, Action<string> log) : void`

- [ ] **Step 1: Write failing tests:**
  - `Apply_sets_ticks_and_clears_apps_the_profile_does_not_list` → returns 1, unlisted app has Kill=false, Restart=false.
  - `Apply_never_ticks_Kill_on_an_app_that_cannot_stop` (StopMethod None, entry Kill=true → Kill false).
  - `Find_is_case_insensitive`; `Upsert_overwrites_by_name_case_insensitively` (count stays 1, entries replaced); `Capture_copies_name_and_ticks`.
  - `Dropped_exe_becomes_an_entry_with_both_ticks` (temp file `tool.exe` → Name `tool`, Commandline `"<path>"`, Kill/Restart true).
  - `Dropped_duplicate_name_is_skipped_and_logged`; `Dropped_folder_and_missing_file_are_ignored`.
- [ ] **Step 2:** Run → compile failure.
- [ ] **Step 3:** Implement by moving code out of the form; form handlers keep only UI (prompt, confirm, rebinding, busy state) and call Core with `LogMessage` as `log`.
- [ ] **Step 4:** Run all tests → pass. `dotnet build` the solution → succeeds.
- [ ] **Step 5:** Update `docs/development/architecture.md` (create it: what Core holds, the `Action<string>` log convention, "Core first, then both apps"). Commit "Move app operations and profiles into Core".

### Task 4: Move workspace operations, sessions and the config watcher into Core

**Files:**
- Create: `src/DevWorkspaceLauncher.Core/WorkspaceController.cs`, `SessionOperations.cs`, `ConfigurationWatcher.cs`
- Modify: `src/DevWorkspaceLauncher.WinForms/Program.cs`
- Test: `tests/DevWorkspaceLauncher.Core.Tests/SessionOperationsTests.cs`, `ConfigurationWatcherTests.cs`

**Interfaces:**
- `WorkspaceController.LoadRows(ConfigurationFile config, bool showAll, IReadOnlyDictionary<string, (bool Close, bool Open)> ticks) : List<WorkspaceRow>` — today's discovery + open probe + tick restore from `RefreshWorkspaces`.
- `WorkspaceController.OpenAsync(IReadOnlyList<(string Uri, string Name)> workspaces, string? vsCodeExecutable, Action<string> log) : Task` (400 ms between opens).
- `WorkspaceController.Launch(WorkspaceRow row, string? vsCodeExecutable, Action<string> log) : void`
- `WorkspaceController.CloseAsync(string name, string? windowTitle, Action<string> log) : Task<bool>` — strict single-match rule, true when closed.
- `WorkspaceController.TogglePreferred(ConfigurationFile config, WorkspaceRow row, Action<string> log) : void`
- `SessionOperations.Find(IEnumerable<WorkspaceProfile>, string? name) : WorkspaceProfile?`
- `SessionOperations.ApplyToTicks(WorkspaceProfile, IDictionary<string, (bool Close, bool Open)> ticks) : void`
- `SessionOperations.ResolveNames(WorkspaceProfile, IEnumerable<WorkspaceRow> rows) : List<(string Uri, string Name)>` (fallback `VsCodeUri.Describe(uri).DisplayName`)
- `SessionOperations.Upsert(List<WorkspaceProfile>, string name, List<string> uris) : WorkspaceProfile`
- `public sealed class ConfigurationWatcher : IDisposable` — ctor `(string path)`; `string LastKnownJson { get; set; }`; `event EventHandler<ConfigurationSnapshot>? Reloaded`; `event EventHandler<string>? Failed`; `void Start()` captures `SynchronizationContext.Current` and raises events on it (directly if null). 400 ms debounce, up to 5 retries on `IOException`, ignores content equal to `LastKnownJson`. Uses an internal `Func<string, ConfigurationSnapshot>` loader (default `AppConfigurationStore.Load(path)`), so `AppConfigurationStore` gains `Load(string path)`.

- [ ] **Step 1: Write failing tests:**
  - `ApplyToTicks_opens_listed_and_clears_others_but_keeps_close` (existing tick `(true, true)` for an unlisted URI → `(true, false)`; listed URI absent before → `(false, true)`).
  - `ResolveNames_prefers_row_names_and_falls_back_to_the_uri`; `Session_Upsert_overwrites_case_insensitively`.
  - `Watcher_raises_Reloaded_after_an_external_write` (temp file; write new JSON; await event within 3 s).
  - `Watcher_ignores_its_own_write` (set `LastKnownJson` to the written JSON → no event within 1.5 s).
  - `Two_watchers_one_writer`: watcher A sets LastKnownJson to what it writes, B does not → B raises, A does not.
- [ ] **Step 2:** Run → compile failure.
- [ ] **Step 3:** Implement; WinForms drops its `FileSystemWatcher`, `_reloadTimer`, `_reloadRetries` and uses `ConfigurationWatcher` (its `SaveConfiguration` sets `LastKnownJson`).
- [ ] **Step 4:** Run all tests → pass. Run the WinForms app (`dotnet run --project src/DevWorkspaceLauncher.WinForms`) and check by hand: Workspaces tab lists entries, editing the config file reloads it, log says so.
- [ ] **Step 5:** Update `architecture.md`. Commit "Move workspace operations, sessions and the config watcher into Core".

### Task 5: Activities model, planner and runner

**Files:**
- Create: `src/DevWorkspaceLauncher.Core/Activities.cs` (model), `ActivityPlanner.cs`, `ActivityRunner.cs`
- Modify: `src/DevWorkspaceLauncher.Core/Configuration.cs` (`ConfigurationFile.Activities`; replace `StopMethodConverter` with a generic `CaseInsensitiveEnumConverter<TEnum>` used by all three enums, keeping the `StopMethod` error text)
- Modify: `devworkspacelauncher.example.json` (add the two example activities from the spec, using its app names)
- Test: `tests/DevWorkspaceLauncher.Core.Tests/ActivityPlannerTests.cs`, `ActivityConfigurationTests.cs`

**Interfaces:**
- `public enum AppAction { Start, Stop, Restart }`, `public enum WorkspaceAction { Open, Close }`
- `public sealed class Activity { required string Name; List<ActivityApp> Apps = []; List<ActivityWorkspace> Workspaces = []; }`
- `public sealed class ActivityApp { required string Name; AppAction Action = Start; }`, `public sealed class ActivityWorkspace { required string Uri; WorkspaceAction Action = Open; }`
- `ConfigurationFile.Activities : List<Activity>` (default empty)
- `public enum ActivityStepKind { StopApp, CloseWorkspace, Wait, StartApp, OpenWorkspace }`
- `public sealed record ActivityStep(ActivityStepKind Kind, string Name, AppConfiguration? App = null, string? Uri = null, string? WindowTitle = null);`
- `public sealed record ActivityPlan(IReadOnlyList<ActivityStep> Steps, IReadOnlyList<string> Notes);`
- `ActivityPlanner.Plan(Activity, ConfigurationFile, Func<AppConfiguration, bool> isRunning) : ActivityPlan`
- `ActivityRunner.RunAsync(Activity, ConfigurationFile, Action<string> log) : Task` — logs `Running activity '{Name}'...`, each note, executes steps via `AppController`/`WorkspaceController` (opens batched through `OpenAsync`), `Task.Delay(1s)` for Wait, ends with `Activity '{Name}' complete.`

Planner rules (spec, Activities): app lookup by name case-insensitive; missing → note `No app named '{x}'; skipped.`; disabled → `{x} is disabled; skipped.`; Stop on `CanStop == false` → `{x} cannot be stopped (StopMethod None); skipped.`; Restart on `CanStop == false` → that note's Restart variant `{x} cannot be stopped (StopMethod None); starting it if not running.` and planned as Start; Start when `isRunning` → `{x} is already running.` and no step. Workspace name from the matching favourite (case-insensitive URI) else `VsCodeUri.Describe(uri).DisplayName`; `WindowTitle` from the favourite. Steps order: StopApp (list order), CloseWorkspace, Wait (only if any StopApp), StartApp (list order; a Restart's start comes here), OpenWorkspace (list order). An activity producing no steps adds note `Nothing to do.`

- [ ] **Step 1: Write failing tests** (planner tests use apps `A` (Process), `B` (Process), `P` (Window), `N` (StopMethod None), and a predicate set):
  - `Restart_stops_waits_then_starts` → kinds `[StopApp A, Wait, StartApp A]`.
  - `Start_skips_an_app_that_is_running` (isRunning A) → no steps, note `A is already running.` plus `Nothing to do.`
  - `Start_runs_an_app_that_is_not_running` → `[StartApp B]`.
  - `Order_is_stops_closes_wait_starts_opens` for Apps `[Start B, Stop A]`, Workspaces `[Open u1, Close u2]` → `[StopApp A, CloseWorkspace u2, Wait, StartApp B, OpenWorkspace u1]`.
  - `Close_only_has_no_wait` → `[CloseWorkspace u2]`.
  - `Unknown_and_disabled_apps_are_noted_and_the_rest_runs`.
  - `Stop_on_StopMethod_None_is_noted`; `Restart_on_StopMethod_None_starts_if_not_running`.
  - `Workspace_name_and_title_come_from_the_favourite`; `Workspace_not_in_favourites_uses_the_uri_name`.
  - `Activities_parse_with_lowercase_actions_and_defaults` — JSON `{ "Activities": [ { "Name": "X", "Apps": [ { "Name": "A", "action": "restart" }, { "Name": "B" } ], "Workspaces": [ { "Uri": "file:///c%3A/x" } ] }, { "Name": "Empty" } ] }` → Restart, Start, Open; `Empty` has two empty lists.
  - `Unknown_action_reports_the_valid_values` → `JsonException` whose message lists `Start, Stop, Restart`.
  - `Activities_round_trip` through `ToJson`/`Parse`.
- [ ] **Step 2:** Run → compile failure.
- [ ] **Step 3:** Implement model, converter, planner, runner.
- [ ] **Step 4:** Run all tests → pass (existing StopMethod converter tests still pass).
- [ ] **Step 5:** Update `architecture.md` (activities section) and the top-level README (Activities section, config fields table). Commit "Add activities".

### Task 6: Activities in the WinForms app

**Files:**
- Modify: `src/DevWorkspaceLauncher.WinForms/Program.cs`

- [ ] **Step 1:** Add a third `FlowLayoutPanel` row "One click activities:" (48 px, above the sessions row; raise `MinimumSize`/`Size` height by 48), rebuilt in `ApplyConfiguration` like the others. Empty text: `No activities yet - add them to the configuration file.` Tooltip: `Run '{Name}'.` Click: guard `_busy`, `SetBusy(true)`, `await ActivityRunner.RunAsync(activity, _config, LogMessage)`, refresh workspaces, `SetBusy(false)`. Buttons are drop targets like the profile buttons; disabled while busy.
- [ ] **Step 2:** Add `"Activities"` to the reload log line: `({n} application(s)..., {p} profile(s), {a} activit(y/ies))` — keep the existing format, append `, {a} activity(ies)`.
- [ ] **Step 3:** Run the app; with the example activities in the shared config, click one and check the log shows the planned steps. Run all tests → pass.
- [ ] **Step 4:** Commit "Show activities in the WinForms app".

### Task 7: WinUI app shell

**Files:**
- Create: `src/DevWorkspaceLauncher.WinUI/DevWorkspaceLauncher.WinUI.csproj`, `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `AppState.cs`, `WindowMessageHook.cs`, `Pages/LogPage.xaml(.cs)`, `Pages/SettingsPage.xaml(.cs)`, `Dialogs.cs`, `app.manifest`
- Modify: `DevWorkspaceLauncher.slnx` (add the project)

**Interfaces:**
- csproj: `OutputType WinExe`, `TargetFramework net10.0-windows10.0.26100.0`, `TargetPlatformMinVersion 10.0.19041.0`, `UseWinUI true`, `WindowsPackageType None`, `WindowsAppSDKSelfContained true`, `Platforms x64`, `RuntimeIdentifier win-x64`, `AppendRuntimeIdentifierToOutputPath false`, `Nullable enable`, `PackageReference Microsoft.WindowsAppSDK 2.5.1` (add `Microsoft.Windows.SDK.BuildTools` only if the build asks for it), ProjectReference Core. `AssemblyName DevWorkspaceLauncher.WinUI`. Publish dir `bin\$(Configuration)\$(TargetFramework)\publish\`.
- `AppState` (single instance owned by `App`): `ConfigurationFile Config`, `string ConfigPath`, `ObservableCollection<string> Log`, `void LogMessage(string)` (prefix `HH:mm:ss: `, also raises `MessageLogged` for the InfoBar), `void Save()` (sets watcher `LastKnownJson`, logs `Saved {path}.`), `event EventHandler? ConfigurationChanged` (after reload), `bool Busy` + `event BusyChanged`, the `ConfigurationWatcher`.
- `WindowMessageHook.Attach(Window window, Action<uint, IntPtr> onMessage)` via `SetWindowSubclass`/`DefSubclassProc` (comctl32), delegate kept alive in a field.
- `Dialogs.PromptAsync(XamlRoot, string title, string label, string initial) : Task<string?>`, `Dialogs.ConfirmAsync(XamlRoot, string title, string message) : Task<bool>` (ContentDialog).
- Code-behind only; no MVVM toolkit. Lists use `ListView` with a column header row built from a `Grid`.

- [ ] **Step 1: Spike the build.** Create the csproj, `App`, an empty `MainWindow`; `dotnet build src/DevWorkspaceLauncher.WinUI` → succeeds with the plain .NET SDK; `dotnet run --project src/DevWorkspaceLauncher.WinUI` → an empty window titled `Developer Workspace Launcher` appears. If the XAML compiler cannot run under `dotnet build`, stop and report.
- [ ] **Step 2:** `MainWindow`: Mica backdrop, `ExtendsContentIntoTitleBar` with a title bar showing `AppIdentity.DisplayName`, `NavigationView` (`PaneDisplayMode Left`, `IsSettingsVisible true`, back button hidden). Menu items in this order: header `Activities` + one item per activity (`SelectsOnInvoked false`, icon Play); `Workspaces`; `Apps`; `Activity log`; header `Sessions` + items (`SelectsOnInvoked false`, icon OpenWith); header `App profiles` + items (`SelectsOnInvoked false`, icon Refresh). Invoking an activity/session/profile item runs it (activity → `ActivityRunner.RunAsync`; session → same as the WinForms one-click session; profile → apply + `KillAndStartAsync`), guarded by `Busy`. Rebuild the dynamic items on `ConfigurationChanged`. Start page: Workspaces. An `InfoBar` at the top of the content frame shows the last `MessageLogged` line (Success severity unless the text starts with `Failed`/`Error`/`Could not`, then Warning).
- [ ] **Step 3:** `LogPage`: the full log in a read-only monospace list, newest at the bottom, auto-scroll. `SettingsPage`: config path (selectable text), `Edit Config` (→ `ConfigFileOpener.Open`), `Open folder`, app version.
- [ ] **Step 4:** `App.OnLaunched`: `AppConfigurationStore.EnsureExists()` (log the line), `Load()`, start the watcher, create the window, attach `WindowMessageHook` → on `UserEnvironment.IsEnvironmentChange` call `UserEnvironment.Refresh()` and log the same text as WinForms. Window activation raises `AppState.WindowActivated` (used by the Workspaces page).
- [ ] **Step 5:** Run the app; check the rail shows the example activities, sessions and profiles; invoking an activity logs its run in the InfoBar and Activity log; editing the config file reloads the rail. Run all tests → pass.
- [ ] **Step 6:** Update `docs/development/README.md` (WinUI build/run/publish, Windows App SDK version, self-contained runtime) and `architecture.md` (how WinUI wires Core). Commit "Add the WinUI app shell".

### Task 8: WinUI Workspaces page

**Files:**
- Create: `src/DevWorkspaceLauncher.WinUI/Pages/WorkspacesPage.xaml(.cs)`, `src/DevWorkspaceLauncher.WinUI/WorkspaceItem.cs` (INotifyPropertyChanged wrapper over `WorkspaceRow`)

- [ ] **Step 1:** List columns: Pref (star toggle; toggling calls `WorkspaceController.TogglePreferred` + `Save` + reload), Close (checkbox, disabled when not open), Open (checkbox), Workspace, Location (tooltip = URI), Status (`Open` or empty), action button (`Focus`/`Launch` → `WorkspaceController.Launch`). Ticks kept in a page-level `Dictionary<string,(bool Close,bool Open)>` that survives refreshes, like WinForms.
- [ ] **Step 2:** `CommandBar`: `Open ticked` (accent), `Close ticked`, `Refresh`, `Show all recent` (AppBarToggleButton), a count label (`{n} preferred`/`{n} recent`), and `Session` (`DropDownButton` with a `MenuFlyout`: one radio item per session to select it, separator, `Load`, `Save As…`, `Update`, `Delete`). Each does what the WinForms handler does, with `Dialogs.PromptAsync`/`ConfirmAsync` and the same log texts.
- [ ] **Step 3:** Refresh on page navigation, on `ConfigurationChanged`, and 300 ms after `WindowActivated` while the page is shown.
- [ ] **Step 4:** Run the app side by side with the WinForms app; both list the same workspaces with the same open states; Launch/Focus, Open ticked, Close ticked, Pref and the session menu all behave as in WinForms. Commit "Add the WinUI Workspaces page".

### Task 9: WinUI Apps page and drag-and-drop

**Files:**
- Create: `src/DevWorkspaceLauncher.WinUI/Pages/AppsPage.xaml(.cs)`, `src/DevWorkspaceLauncher.WinUI/AppItem.cs`

- [ ] **Step 1:** List of non-disabled apps: Application, Kill (checkbox; disabled with tooltip `StopMethod is None: this application is never stopped.` when `!CanStop`), Restart (checkbox). Ticks write straight to the `AppConfiguration` objects, as WinForms binding does (session-only; not saved).
- [ ] **Step 2:** `CommandBar`: `Kill & Start Selected` (accent), `Restart Selected`, `Edit Config`, and `Profile` (`DropDownButton`: radio item per profile, separator, `Load`, `Save As…`, `Update`, `Delete`), same texts and confirmations as WinForms. Busy disables the run buttons.
- [ ] **Step 3:** Drag and drop on the window root (`AllowDrop`, `DragOver` accepts `StorageItems` as Copy, `Drop` → `DroppedApps.Add` with the item paths → `Save` → reload the list). Works on every page.
- [ ] **Step 4:** Run; tick changes, both run buttons, the profile menu and dropping an `.exe` and a `.lnk` behave as in WinForms. Run all tests → pass. Commit "Add the WinUI Apps page and drag-and-drop".

### Task 10: Docs, migration on this machine, final verification

**Files:**
- Modify: `README.md`, `docs/development/README.md`, `docs/development/architecture.md`

- [ ] **Step 1:** README: new name and intro (two apps, both current; WinUI is the trial), screens per app, Activities, configuration location and migration, build/run/publish for both. Remove "(.NET version only)" markers that no longer distinguish anything, keep the PowerShell prototype notes.
- [ ] **Step 2:** Ask the author before copying `bin\Release\net10.0-windows\publish\restartapps.json` (repo root, the live file) to `%APPDATA%\DevWorkspaceLauncher\devworkspacelauncher.json`. Do not overwrite an existing shared file.
- [ ] **Step 3:** `dotnet build DevWorkspaceLauncher.slnx -c Release` → succeeds; `dotnet test tests/DevWorkspaceLauncher.Core.Tests` → all pass; `dotnet publish` both apps → output folders contain no spaces.
- [ ] **Step 4:** By hand, both apps running together: same config, an edit in one reloads the other; an activity with `Start` on an already-running app and on a running PWA does not start a second copy; environment change (`[Environment]::SetEnvironmentVariable('DWL_TEST','1','User')`) is logged by both.
- [ ] **Step 5:** Commit "Document both apps and activities".
