---
id: SWAN-1.1
title: Agent working rules in AGENTS.md
status: done
parent: SWAN-1
---

# SWAN-1.1: Agent working rules in AGENTS.md

Intent: Add project rules OUTSIDE the OpenRig managed blocks in AGENTS.md (do not duplicate rig norms already there: no-push/publish, docs flow).

Rules to add:
- Always format before commit (dotnet format; Prettier/ESLint for UI)
- TDD (failing test first)
- Playwright for UI/E2E tests
- Conventional Commits
- Commit small and often

## Acceptance criteria

- [x] Rules section present outside managed blocks
- [x] Names the exact format/test commands (fill in once tasks 2/3 exist, or as planned commands)
- [x] No overlap with existing managed-block rules

## Files to touch

- `AGENTS.md`

## Notes

Do not touch CLAUDE.md — it is OpenRig-managed.
