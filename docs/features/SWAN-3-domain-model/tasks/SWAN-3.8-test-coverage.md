---
id: SWAN-3.8
title: Invariant test coverage + child round-trips
status: todo
parent: SWAN-3
---

# SWAN-3.8: Invariant test coverage + child round-trips

Intent: Fill test coverage gaps. SWAN-3.1-3.7 acceptance criteria stated "one test per invariant," but review found many invariants without dedicated tests. Add focused, named tests for each invariant; expand migration tests to round-trip child entities and value objects, not just root counts.

## Invariant-specific tests (one test per, with clear name)

**Roster context:**
- [ ] Roster enforces exactly 9 distinct teams at close
- [ ] Roster changes only while window open
- [ ] Roster admin correction requires reason field

**Ballot context:**
- [ ] Ballot max 3 distinct roster teams
- [ ] Ballot no bye team allowed
- [ ] Ballot no changes allowed after pick's LockTime
- [ ] Ballot dog count 1 or 2 by week flag
- [ ] Ballot dogs have distinct teams AND games
- [ ] Ballot every change appended to history
- [ ] Ballot complete flag set when 3 picks submitted

**Season context:**
- [ ] Season pool stakes sum to buy-in amount
- [ ] Season has ≥3 double-dog weeks
- [ ] Season structural settings only in Setup phase

**Membership context:**
- [ ] Membership deactivate preserves history

**Team context:**
- [ ] Team must have a division assigned

**Announcement context:**
- [ ] Announcement schedule must be future datetime
- [ ] Announcement sends once (idempotent)

**Notification context:**
- [ ] NotificationPreferences: essential categories cannot be muted (per PRD §8 M10)

**PushSubscription context:**
- [ ] PushSubscription endpoint uniqueness (test DB index)

## Migration round-trip tests

Expand `Swan.Services.Tests` with focused round-trip tests:
- [ ] Ballot with team picks + dog picks and full change history round-trips
- [ ] Week with games, approved lines, and DogOption round-trips
- [ ] Ledger with multiple items round-trips
- [ ] Season with all settings VOs round-trips

Assert values (not just counts); verify child collections and nested VOs.

## Acceptance criteria

- [ ] Each invariant has a focused, named test in `Swan.Core.Tests`
- [ ] Tests use TDD: if a test exposes a missing invariant in the code, fix the code to enforce it
- [ ] Four migration round-trip tests in `Swan.Services.Tests` cover child entities and VOs
- [ ] `dotnet test Swan.slnx` passes all tests
- [ ] `dotnet format --verify-no-changes` clean

## Files to touch

- `tests/Swan.Core.Tests/` (add invariant-focused tests)
- `tests/Swan.Services.Tests/` (extend round-trip migration tests)
- `src/Swan.Core/` (only where a test exposes a gap that needs fixing)
