---
id: SWAN-3.4
title: Picks and Scoring aggregates
status: done
parent: SWAN-3
---

# SWAN-3.4: Picks and Scoring aggregates

Intent: Implement Picks and Scoring contexts: Roster (member teams), Ballot (picks with change history and locktime enforcement), and WeekScore (scoring container).

## Aggregates

**Picks context:**
- `Roster` — season + member, exactly 9 distinct teams at window close, changes only while window open, admin correction with reason field
- `Ballot` — member + week, team picks (max 3 distinct teams), dog picks (1 or 2 depending on week flag, distinct teams and games), complete flag (true when 3 picks submitted), change history (every change appended)
- `TeamPick`, `DogPick` — pick values with LockTime (passed in by caller, used to enforce no changes at/after the LockTime)
- Events: `PickSubmitted`, `PickChanged`

**Scoring context:**
- `WeekScore` — season + member + week, container for score lines (replace-all on recompute), no scoring math (returns Result for changes)

## Acceptance criteria

- [x] Roster enforces exactly 9 teams at close; window-open check prevents mutations outside window
- [x] Roster admin correction requires a reason field
- [x] Ballot: max 3 distinct teams across picks; no bye team; dog count 1 or 2 by week flag
- [x] Ballot: dogs have distinct teams and games
- [x] Ballot: no changes allowed at or after a pick's LockTime (compare against passed-in `now`)
- [x] Ballot: every change appended to history (complete audit trail)
- [x] Ballot: complete flag set when 3 picks submitted
- [x] WeekScore: replace-all lines on recompute (idempotent)
- [x] WeekScore: no scoring math (just a container)
- [x] Events `PickSubmitted` and `PickChanged` raised and collected
- [x] `dotnet test` passes; `dotnet format` clean

## Files to touch

- new `src/Swan.Core/Picks/`
- new `src/Swan.Core/Scoring/`
- `tests/Swan.Core.Tests/` (add context tests)
