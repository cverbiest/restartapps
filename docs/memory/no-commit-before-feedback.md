---
name: no-commit-before-feedback
description: "Do not commit right after implementing; wait for the user's feedback/approval first"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 05dab810-c527-444a-b4f5-01eb29c4654b
  modified: 2026-10-26T06:31:07.394Z
---

Don't commit as soon as a change builds and tests pass. Show the change, let the user review and test, and commit only when they say so (2026-10-26).

**Why:** the user wanted to review first; e.g. they did not want tools/Export-RdpPlusFavorites.ps1 (which uses RDP+'s decryption) in the repo, and had to reset and gitignore it themselves.

**How to apply:** finish the work, report it, ask "commit?" or wait for the go-ahead. Also be cautious about adding to the repo anything that touches third-party crypto or private data.
