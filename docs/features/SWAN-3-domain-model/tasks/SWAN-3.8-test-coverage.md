---
id: SWAN-3.8
title: Invariant test coverage + child round-trips
status: done
parent: SWAN-3
---

# SWAN-3.8: Invariant test coverage + child round-trips

Intent: Fill test coverage gaps. SWAN-3.1-3.7 acceptance criteria stated "one test per invariant," but review found many invariants without dedicated tests. Add focused, named tests for each invariant; expand migration tests to round-trip child entities and value objects, not just root counts.

## Invariant-specific tests (one test per, with clear name)

**Roster context:**
- [x] Roster enforces exactly 9 distinct teams at close
- [x] Roster changes only while window open
- [x] Roster admin correction requires reason field

**Ballot context:**
- [x] Ballot max 3 distinct roster teams
- [x] Ballot no bye team allowed
- [x] Ballot no changes allowed after pick's LockTime
- [x] Ballot dog count 1 or 2 by week flag
- [x] Ballot dogs have distinct teams AND games
- [x] Ballot every change appended to history
- [x] Ballot complete flag set when 3 picks submitted

**Season context:**
- [x] Season pool stakes sum to buy-in amount
- [x] Season has ≥3 double-dog weeks
- [x] Season structural settings only in Setup phase

**Membership context:**
- [x] Membership deactivate preserves history

**Team context:**
- [x] Team must have a division assigned

**Announcement context:**
- [x] Announcement schedule must be future datetime
- [x] Announcement sends once (idempotent)

**Notification context:**
- [x] NotificationPreferences: essential categories cannot be muted (per PRD §8 M10)

**PushSubscription context:**
- [x] PushSubscription endpoint uniqueness (test DB index)

## Migration round-trip tests

Expand `Swan.Services.Tests` with focused round-trip tests:
- [x] Ballot with team picks + dog picks and full change history round-trips
- [x] Week with games, approved lines, and DogOption round-trips
- [x] Ledger with multiple items round-trips
- [x] Season with all settings VOs round-trips

Assert values (not just counts); verify child collections and nested VOs.

## Acceptance criteria

- [x] Each invariant has a focused, named test in `Swan.Core.Tests`
- [x] Tests use TDD: if a test exposes a missing invariant in the code, fix the code to enforce it
- [x] Four migration round-trip tests in `Swan.Services.Tests` cover child entities and VOs
- [x] `dotnet test Swan.slnx` passes all tests
- [x] `dotnet format --verify-no-changes` clean

## Files to touch

- `tests/Swan.Core.Tests/` (add invariant-focused tests)
- `tests/Swan.Services.Tests/` (extend round-trip migration tests)
- `src/Swan.Core/` (only where a test exposes a gap that needs fixing)
