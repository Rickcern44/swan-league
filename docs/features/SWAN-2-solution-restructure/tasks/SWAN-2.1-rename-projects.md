---
id: SWAN-2.1
title: Rename existing projects
status: done
parent: SWAN-2
---

# SWAN-2.1: Rename existing projects

Intent: Rename Api → Swan.Api, web → Swan.Web, Api.Tests → Swan.Api.Tests. Update all namespaces, apphost.cs references, README and AGENTS.md commands.

## Acceptance criteria

- [x] `dotnet build` and `dotnet test` pass
- [x] `npm lint`, `npm run format:check`, `npm test` (Vitest) pass
- [x] Playwright e2e passes from new paths
- [x] `aspire run` starts both UI and API
- [x] No stale "Api"/"web" paths remain (grep clean)
- [x] Solution file renamed to Swan.slnx if needed

## Files to touch

- `src/Api` → `src/Swan.Api` (with namespace updates)
- `src/web` → `src/Swan.Web` (with namespace updates)
- `tests/Api.Tests` → `tests/Swan.Api.Tests` (with namespace updates)
- `SwanLeagueTracker.slnx` (rename or update)
- `apphost.cs`
- `README.md`
- `AGENTS.md`

## Blocked

`aspire start --non-interactive --format Json` fails before resource startup with `CS2012` on the user-level file-based AppHost compilation cache at `~/Library/Application Support/dotnet/runfile/.../apphost.dll`. The same failure occurs after `aspire stop` and with `--isolated`; `aspire doctor` reports the AppHost and DCP healthy. The renamed solution still builds and tests successfully.

## Recovery note

After a sandboxed .NET build, run `dotnet build-server shutdown` before starting the AppHost; AppHost builds require escalated permission to write their user-level runfile cache.
