---
id: SWAN-5.7
title: Competition ranking for tied standings
status: done
parent: SWAN-5
---

# SWAN-5.7: Competition ranking for tied standings

Intent: Decide how tied coaches are ranked in standings (PRD §6.1 step 2).

## Decision

The owner chose traditional competition ranking: equal scores share a rank and the next rank skips the occupied places (for example `1, 1, 1, 4`). No secondary tiebreak rule is applied; tied coaches remain tied and `TiebreakUnbroken` stays true for them. No TiebreakPolicy class was added and no external rules document is needed.

## Delivered

- `StandingsService` already produced this ranking; no implementation change.
- Regression test: `StandingsUseCompetitionRankingSoTiesShareARankAndSkipTheNext` in `tests/Swan.Services.Tests/SwanDbContextTests.cs` (three equal scores and one lower score give ranks `1, 1, 1, 4`).

## Out of scope

- R18 color-tier configuration (separate; PRD unchanged).
