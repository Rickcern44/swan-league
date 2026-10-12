---
id: SWAN-2.1
title: Rename existing projects
status: todo
parent: SWAN-2
---

# SWAN-2.1: Rename existing projects

Intent: Rename Api → Swan.Api, web → Swan.Web, Api.Tests → Swan.Api.Tests. Update all namespaces, apphost.cs references, README and AGENTS.md commands.

## Acceptance criteria

- [ ] `dotnet build` and `dotnet test` pass
- [ ] `npm lint`, `npm run format:check`, `npm test` (Vitest) pass
- [ ] Playwright e2e passes from new paths
- [ ] `aspire run` starts both UI and API
- [ ] No stale "Api"/"web" paths remain (grep clean)
- [ ] Solution file renamed to Swan.slnx if needed

## Files to touch

- `src/Api` → `src/Swan.Api` (with namespace updates)
- `src/web` → `src/Swan.Web` (with namespace updates)
- `tests/Api.Tests` → `tests/Swan.Api.Tests` (with namespace updates)
- `SwanLeagueTracker.slnx` (rename or update)
- `apphost.cs`
- `README.md`
- `AGENTS.md`
