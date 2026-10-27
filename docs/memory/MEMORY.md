# Project memory

Facts, decisions and working agreements for this project, kept in the repo so they travel with it.
One fact per file; this index has one line per file. Claude reads this index at the start of a
session (via `CLAUDE.md`) and opens the files it needs.

- [RDP launcher plan](rdp-launcher-plan.md) — doc link + brainstorming decisions (creds by reference, ignore mstsc state)
- [App rename](app-rename-expected.md) — new name "Developer Workspace Launcher" (chosen 2026-10-25); keep name out of persisted ids
- [Tray is the daily UI](tray-is-daily-ui.md) — RDP+ tray is main UI; plan phase order is suspect, re-check with user
- [No commit before feedback](no-commit-before-feedback.md) — wait for the user's review/go-ahead before committing
- [Todo list](todo-list.md) — keep docs/superpowers/plans/todo.md updated, mark done items, never drop the user's items
- [Publish before feedback](publish-before-feedback.md) — `dotnet publish` the app the user runs before asking them to try a change
- [Consistent UX across tabs](consistent-ux-across-tabs.md) — same layout, search, multi-select, pins, sorting on every grid; differ only when relevant
