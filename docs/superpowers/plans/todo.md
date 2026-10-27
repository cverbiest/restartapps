# todo's

## startable items

These include
* rdp sessions **done**: RDP sessions page, one-click items on the rail, `rdp` command line
  * RDP sessions in the configuration file, credentials by reference to Windows Credential Manager **done**
  * Credentials button on the RDP sessions page (opens Credential Manager) **done**
  * Import of Remote Desktop Plus favorites (sessions and options, not profiles) **done**
  * `/title` and `/drives`, the options the RDP+ favorites use that were missing **done**
  * RDP session editor: Credential is a dropdown of the existing `DevLauncher_*` generic credentials,
    showing the username each logs in as; any other name can still be typed **done**
* profiles
* vscode workspaces

can be marked as favorite = on start page and tray **done** (pin on All items, Activities and RDP sessions)

## Start page

* buttons/icons to start items, **done** (a tile per favorite)
* order of the start items **done** (drag tiles, or right-click: move first / earlier / later / last,
  unpin; the tray menu follows the same order, a new pin goes last)
* some status info, e.g. current active networks : wired, wifi, vpn

## Launcher window

* Don't list launchable items on the left (the rail); they belong on their own tabs **done**
* Add an "All items" tab **done**
* Add an Activities tab (activities were only on the rail) **done**
* All items: no visible difference between favorite and not; use colour **done** (orange pin)
* All items: apps are missing; a favorite app only starts the app, or brings it to the front when it
  is already running (it never kills) **done**
* Multi-select in every grid where it makes sense, used for: **done** (All items, Activities, Apps,
  Workspaces, RDP sessions)
  * delete **done**
  * add to activity **done**
* Quick search field on every grid **done** (also on the Activity log)

## VS Code workspaces

* Rename the Workspaces tab to VS Code workspaces **done**
* The grid is not like the others **done** (intro line, coloured star, no per-row Launch button:
  double-click opens or focuses, as the other grids run their row)
* "develop" on Windows and on cce-dev03 look identical: show the host in the name **done**
  ("develop (cce-dev03)"; also in the activity editor)
* If possible a Last used column, and sorting by clicking a column header **done**
* Keep the UX similar across tabs, differ only when relevant: sorting on every grid **done** (All items,
  Activities, Apps, VS Code workspaces, RDP sessions; the pin/star column sorts favorites first); every
  grid keeps its selection when it refreshes **done**

## App definitions & profiles

* Split the app definitions and the app profile definitions — decided 2026-10-27: app profiles and
  workspace sessions become activities (see "Activities are the only group"); the Apps tab keeps only
  the app definitions **done**
* Allow dropping items on the app definitions **done** (dropping anywhere on the window adds the app)

## Activities are the only group

* Convert the app profiles and workspace sessions into activities with the same names, once, keeping a
  backup of the old file **done**
* An activity holds any item: apps (start / stop / restart), workspaces (open / close), RDP sessions
  (connect); stored so activities inside activities can come later **done**
* Activities tab: create, rename, delete; edit an activity's items and what it does with each **done**
* Apps tab: only the app definitions (add, edit, delete, drop exe/shortcut files); multi-select with
  Start / Stop / Restart instead of the Kill/Restart ticks and the Profile menu **done**
* Workspaces tab: the Session menu goes **done** (Open / Close act on the selected rows, no more ticks)
* Running "Develop workspaces" did not open the cce-dev03 workspace: when the launcher was started from
  inside VS Code, the `ELECTRON_RUN_AS_NODE` it inherited made Code.exe exit without opening anything
  **done** (no longer passed on to VS Code)
* Later: activities inside activities

## system tray
* System tray (WinUI): open launcher, Start with Windows (`/tray`),
  Exit. Closing the window hides it to the tray; the app runs as a single instance; problems while
  hidden show as a balloon **done**
* Use or create another icon: there are too many blue icons already **done** (orange rocket)
* Single-level menu: Open launcher, the favorites (= the start page items), Start with Windows and
  Exit **done**
* favorite items **done**

## profile managment

Profiles become activities (see "Activities are the only group"); these items then apply to activities.

* Tab page to edit profiles
* add/remove profiles
* add / update items on profile

# Monitor dependant size profile
probably already (partially) present

Partial: size presets exist (`RdpSizes`, built-in `fit68` and `full`) and a session can pick a monitor
(`Monitor`), but a preset is not yet chosen by which monitors are connected.

# WinForms app

* Update it to the new configuration: after app profiles and workspace sessions became activities it
  no longer sees them (accepted for now, 2026-10-27)
