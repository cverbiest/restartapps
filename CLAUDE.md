# CLAUDE.md

## Project memory

Project memories live in the repo, in `docs/memory/`, not in the per-machine Claude memory folder,
so they are the same on every computer.

- Read the index at the start of a session: @docs/memory/MEMORY.md
- Save new memories there: one fact per file with the usual frontmatter (`name`, `description`,
  `metadata.type`), plus one line in `docs/memory/MEMORY.md`. Update an existing file rather than
  adding a duplicate; delete one that turns out to be wrong.
- Memory changes are part of the working tree like any other change: don't commit them before the
  user has reviewed them.
