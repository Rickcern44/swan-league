---
id: SWAN-5.0
title: Housekeeping — delete stray folder, add gitignore
status: done
parent: SWAN-5
---

# SWAN-5.0: Housekeeping

## Task

Delete stray folder `src/Swan.Data/bin\Debug/` (Roslyn BuildHost output with literal backslash, from dotnet format). Add .gitignore pattern to prevent recurrence.

## Acceptance criteria

- [x] Folder deleted
- [x] .gitignore pattern added (covers `bin\` and similar spurious build folders)
- [ ] `git status` clean — local workflow configuration is excluded from version control.
