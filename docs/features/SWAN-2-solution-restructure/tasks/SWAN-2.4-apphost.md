---
id: SWAN-2.4
title: AppHost resources (Postgres, Mailpit, wiring)
status: blocked
parent: SWAN-2
---

# SWAN-2.4: AppHost resources (Postgres, Mailpit, wiring)

Intent: Update apphost.cs to add Postgres container + database, Mailpit, and wire them to Api/Worker/Web per PRD §10.3. Api references Mailpit and database; Worker references database; Web references Api.

## Acceptance criteria

- [ ] `aspire run` shows postgres, mailpit, api, worker, web all healthy
- [x] Api /health endpoint includes a database connectivity check
- [ ] Mailpit UI is reachable from the dashboard
- [ ] All containers start cleanly with no errors

## Files to touch

- `apphost.cs`

## Skills

- `.agents/skills/aspire-orchestration`

## Notes

Reference PRD §10.3 for the exact wiring diagram. Use Aspire Postgres + Mailpit container specs.

## Blocked

The AppHost compiles and exposes postgres, swan, mailpit, api, worker, and web resources, but this machine has no `docker` command or running Docker-compatible daemon. Postgres and Mailpit cannot start, so their health, Mailpit UI reachability, and clean container startup remain unverified.
