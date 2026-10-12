---
id: SWAN-3.3
title: Schedule aggregates
status: done
parent: SWAN-3
---

# SWAN-3.3: Schedule aggregates

Intent: Implement Schedule context aggregates: Team, SeasonTeam, Week (with games, line history, and state transitions).

## Aggregates (PRD §9.6)

**Schedule context:**
- `Team` — name, logo (nullable), created date
- `SeasonTeam` — team + season, division, finish position (points derived from week scores), finish ties
- `Week` — season, week number, type (Regular/Playoff/SuperBowl with picks-required flag), games list, line history, approved lines (frozen post-publish)
- `Game` — home team, away team, kickoff, line updates (history)
- `DogOption` — team ID
- `WeekType` — enum with playoff round info, picks-required flag per PRD §9.6

State machine: `Draft` → `LinesApproved` → `Open` → `Locked` → `Final`, with admin reopen allowed.

## Acceptance criteria

- [x] State machine enforced (Draft→LinesApproved→Open→Locked→Final) with admin reopen
- [x] Publish blocked until every non-excluded game has an approved line
- [x] Approved lines frozen after publish (no changes allowed)
- [x] GameResult requires both scores; SeasonTeam point value derived from week scores
- [x] "One team per finish per division" invariant testable as static validation on the set (used later by wizard UI)
- [x] Events `WeekPublished`, `WeekLocked`, `ResultsConfirmed` raised and collected
- [x] Playoff weeks include round info and picks-required flag
- [x] `dotnet test` passes; `dotnet format` clean

## Files to touch

- new `src/Swan.Core/Schedule/`
- `tests/Swan.Core.Tests/` (add Schedule tests)
