---
name: tray-is-daily-ui
description: "The system tray is the user's main daily UI for RDP (as with RDP+); plan phase priorities are suspect"
metadata:
  node_type: memory
  type: user
  originSessionId: 05dab810-c527-444a-b4f5-01eb29c4654b
  modified: 2026-10-26T05:36:30.606Z
---

The user's daily interface to RDP+ is its system tray menu, not a main window. The RDP plan doc put the tray last (Phase 6), which the user says is wrong, and other phase priorities there may be wrong too (2026-10-26).

**Why:** the plan was ordered by implementation cost, not by how the user actually works.

**How to apply:** treat the tray (favorites, sessions, active-session switching) as a near-term priority. Re-check any ordering in [[rdp-launcher-plan]] with the user rather than trusting it. Their todo list (docs/superpowers/plans/todo.md) wants favorites shown on a start page and in the tray.
