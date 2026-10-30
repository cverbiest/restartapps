---
name: consistent-ux-across-tabs
description: Keep the tabs/grids of the launcher alike (layout, toolbar, search, multi-select, pins, sorting); differ only when relevant
metadata:
  type: feedback
---

Keep the UX similar across the launcher's tabs: same page layout (title, intro line, toolbar, search box
on the right, grid with header row), multi-select, coloured pin/star, double-click runs or opens the row,
click a column header to sort. A tab differs only where its content makes that relevant.

**Why:** the user asked for it on 2026-10-27, after noticing the Workspaces grid looked unlike the others
and after sorting was added to one grid only.

**How to apply:** when a feature lands on one grid, add it to the other grids in the same change, or ask
when it doesn't obviously fit there. Related: [[tray-is-daily-ui]].
