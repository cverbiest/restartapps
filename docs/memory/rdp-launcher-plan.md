---
name: rdp-launcher-plan
description: Where the RDP launcher (RDP+ parity) feature plan lives and the decisions made so far
metadata:
  node_type: memory
  type: project
  originSessionId: ceb44fee-edb7-469b-8cd6-d7681d144603
  modified: 2026-10-13T05:40:42.381Z
---

The RDP launcher feature plan is the Claude Docs doc "RDP Launcher – Feature Plan (RDP+ parity)": https://claude.ai/artifact/DFk5rmti5jhFasDC83kdaU (doc id 633ca0c5-59cd-4ee8-a7e9-736bbe5e9d9d).

Decisions from brainstorming on 2026-10-13:
- The first slice is the engine plus core CLI switches plus a minimal RDP Sessions tab.
- Session definitions go in the same config file as the apps (restartapps.json), and they only *reference* credentials. The app never stores passwords itself.
- There's no import/export from RDP+ unless asked. The app has its own credentials and doesn't reuse RDP+ ones.
- mstsc stays the client, but none of its saved state is trusted. Write explicit .rdp settings and never read, write or delete mstsc "Remember me" TERMSRV/* entries.

**Why:** user wants RDP+ replaced by their own launcher without coupling to RDP+ or mstsc state.

**How to apply:** check this before designing RDP features. See also [[app-rename-expected]].
