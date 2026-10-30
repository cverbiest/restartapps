---
name: publish-before-feedback
description: Republish the build the user actually runs before asking them to try a change
metadata:
  type: feedback
---

Before asking the user to try or review a change, run `dotnet publish -c Release` for the app they run, so the build in its `bin\Release\...\publish` folder contains the change. A plain `dotnet build` only updates `bin\Debug`, which they don't run.

If a running copy locks the publish folder, stop it yourself (`Stop-Process` on `DevWorkspaceLauncher.WinUI`),
then publish and start it again; don't ask the user to close it (they asked for this, 2026-10-29).

**Why:** on 2026-10-26 the tray icon was built only to Debug. The user restarted the published app several times and could not find the tray (2026-10-26).

**How to apply:** treat "published and started" as part of finishing any user-visible change, before asking for feedback. This is how [[no-commit-before-feedback]] reviews happen.
