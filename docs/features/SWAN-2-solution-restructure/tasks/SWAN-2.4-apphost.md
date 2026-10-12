---
id: SWAN-2.4
title: AppHost resources (Postgres, Mailpit, wiring)
status: todo
parent: SWAN-2
---

# SWAN-2.4: AppHost resources (Postgres, Mailpit, wiring)

Intent: Update apphost.cs to add Postgres container + database, Mailpit, and wire them to Api/Worker/Web per PRD §10.3. Api references Mailpit and database; Worker references database; Web references Api.

## Acceptance criteria

- [ ] `aspire run` shows postgres, mailpit, api, worker, web all healthy
- [ ] Api /health endpoint includes a database connectivity check
- [ ] Mailpit UI is reachable from the dashboard
- [ ] All containers start cleanly with no errors

## Files to touch

- `apphost.cs`

## Skills

- `.agents/skills/aspire-orchestration`

## Notes

Reference PRD §10.3 for the exact wiring diagram. Use Aspire Postgres + Mailpit container specs.
