---
id: SWAN-3.2
title: Identity, League, Platform aggregates
status: todo
parent: SWAN-3
---

# SWAN-3.2: Identity, League, Platform aggregates

Intent: Implement three core aggregates: User (with login token lifecycle), Season (with settings and rules), and infrastructure aggregates (Membership, AuditEntry, ScheduledJob).

## Aggregates (PRD §9.3)

**Identity context:**
- `User` — email, name, login tokens (single-use, 15-min expiry using passed-in `now`)
- `LoginToken` — token string, used flag, expiry
- Events: `MembershipInvited`

**League context:**
- `Season` — SeasonSettings (buy-in, staleness window, double-dog count 2–4), PlayoffRules, RosterWindow (dates), PoolDefinition, Ruling (league-wide decisions)
- `Membership` — user + season, role, badge awards
- Events: `SeasonActivated`

**Platform context:**
- `AuditEntry` — immutable log of admin actions (user, action, timestamp, resource ID, details); no mutators
- `ScheduledJob` — key, locked by node ID + timestamp (single claim, lease expires), next run time

## Acceptance criteria

- [ ] Unit tests cover all invariants from PRD §9.3 (e.g., pool stakes sum to buy-in; ≥3 double-dog weeks; structural settings only during Setup phase)
- [ ] LoginToken is single-use and expires 15 min after issue (using passed-in `now` time)
- [ ] Season state machine (Setup → Live → Completed) enforced
- [ ] Membership role enum covers all roles
- [ ] ScheduledJob lease claim prevents concurrent execution (single node holds the lease)
- [ ] Events `MembershipInvited` and `SeasonActivated` are raised and collected
- [ ] `dotnet test` passes; `dotnet format` clean

## Files to touch

- new `src/Swan.Core/Identity/`
- new `src/Swan.Core/League/`
- new `src/Swan.Core/Platform/`
- `tests/Swan.Core.Tests/` (or per-context test folders)
