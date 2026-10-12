---
id: SWAN-1.3
title: UI app + AppHost wiring
status: done
parent: SWAN-1
---

# SWAN-1.3: UI app + AppHost wiring

Intent: React+Vite+TS app that calls the API sample endpoint and renders the result; wired in apphost.cs with reference to API (service discovery/proxy).

## Acceptance criteria

- [x] `aspire run` starts UI and API
- [x] UI page shows API response
- [x] `npm run lint` passes
- [x] `npm run format:check` passes
- [x] `npm test` (Vitest) passes

## Files to touch

- `apphost.cs`
- new `src/web`

## Skills

- `.agents/skills/aspire-orchestration`
