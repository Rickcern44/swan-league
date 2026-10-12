---
id: SWAN-2.3
title: Worker host skeleton
status: done
parent: SWAN-2
---

# SWAN-2.3: Worker host skeleton

Intent: Create Swan.Worker as a BackgroundService that polls every 15s and calls a no-op "run due jobs" handler in Swan.Services. Support `--once` flag to run once and exit (PRD §10.2).

## Acceptance criteria

- [x] References Swan.Services and Swan.ServiceDefaults
- [x] `dotnet run --project src/Swan.Worker -- --once` exits with code 0
- [x] Unit test covers the --once path and passes
- [x] Worker appears in apphost.cs
- [x] `dotnet build` and `dotnet test` pass

## Files to touch

- new `src/Swan.Worker`
- new `tests/Swan.Worker.Tests`
- `apphost.cs`
- `Swan.slnx` (update project references)

## Notes

No job table yet. This is just the polling skeleton with a no-op handler.
