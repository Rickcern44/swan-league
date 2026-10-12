---
id: SWAN-5.1
title: LockPolicy
status: todo
parent: SWAN-5
---

# SWAN-5.1: LockPolicy

Intent: Compute LockTime per pick (PRD R6, R20, §4 lock behavior). Team pick locks at min(team kickoff, week deadline); dog pick locks at its game kickoff. Answers CanChange(pick, now). Deadline from season setting (default Sun 1:00 PM ET), stored UTC.

## Design

- **LockPolicy** — pure function LockTime(pick, seasonSettings, weekStartUtc) → DateTime UTC
- **CanChange** — bool CanChange(pick, lockTime, now) → lockTime > now
- **Week deadline** — season setting (default Sun 1:00 PM ET)
- **Time zones** — all stored/returned in UTC; tests use ET to verify offset

## Acceptance criteria

- [ ] Team pick lock = min(team's game kickoff, week deadline)
- [ ] Dog pick lock = its game's kickoff
- [ ] Tests for:
  - [ ] Thursday/Friday/Saturday games
  - [ ] Sunday 9:30 AM ET London game
  - [ ] Normal Sunday 1 PM+ games
  - [ ] Monday-night dog (R8)
  - [ ] DST boundary weeks (ET offset correct)
- [ ] Ballot uses LockPolicy output (replace any caller-supplied LockTime path where sensible)
- [ ] `dotnet test` passes

## Files to touch

- `src/Swan.Core/Picks/` (LockPolicy)
- `src/Swan.Core/Picks/Ballot.cs` (replace hardcoded lock logic)
- `tests/Swan.Core.Tests/` (LockPolicy tests)
