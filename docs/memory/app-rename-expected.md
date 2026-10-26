---
name: app-rename-expected
description: "RestartApps is being renamed to \"Developer Workspace Launcher\" — keep the app name out of persisted identifiers"
metadata:
  node_type: memory
  type: project
  originSessionId: cba31352-7ed9-4ada-bf4a-bb856ae4d218
  modified: 2026-10-25T08:00:27.717Z
---

On 2026-10-25 the user chose the new name "Developer Workspace Launcher" (replacing RestartApps), decided during brainstorming for the WinUI 3 side-by-side version. Earlier (2026-10-13) the placeholder idea was SessionAppLauncher.

**Why:** the name no longer describes the tool once it launches VS Code workspaces and RDP sessions (see [[rdp-launcher-plan]]).

**How to apply:** route the app name through one constant/property; don't scatter it through persisted names (Credential Manager targets, %APPDATA% folders, config keys, URI schemes). Existing restartapps.json needs a migration path when the rename lands.
