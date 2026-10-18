---
id: SWAN-1.2
title: API service + AppHost wiring
status: done
parent: SWAN-1
---

# SWAN-1.2: API service + AppHost wiring

Intent: Minimal API project with /health and one sample GET endpoint (e.g. /api/ping), xUnit test project, added to apphost.cs.

## Acceptance criteria

- [x] `dotnet test` passes with >=1 endpoint test written first
- [x] `aspire run` shows API healthy in dashboard
- [x] `dotnet format --verify-no-changes` clean
- [x] solution/slnx file at repo root

## Files to touch

- `apphost.cs`
- `aspire.config.json`
- new `src/Api`
- new `tests/Api.Tests`
