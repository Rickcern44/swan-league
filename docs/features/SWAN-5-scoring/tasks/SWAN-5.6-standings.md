---
id: SWAN-5.6
title: Standings read models
status: done
parent: SWAN-5
---

# SWAN-5.6: Standings read models

Intent: Standings queries for Main, Dog, Regular Season, Playoff pools (PRD C10): rank, points, wins, current week and season-to-date, from stored WeekScores. Tied coaches share a rank (competition ranking, see [SWAN-5.7](SWAN-5.7-tiebreak.md)) and are marked "unbroken".

## Pools

- Main pool
- Dog pool
- Regular season pool (excludes playoffs)
- Playoff pool (R12 qualification + R13 multipliers)

## Acceptance criteria

- [x] Read model computes rank, points, wins per pool
- [x] Ties share a rank, marked "unbroken"
- [x] Testcontainers test: seed weeks, assert totals and ranks
- [x] Dog points count in the Dog pool only (R15, fixed; no setting)
- [x] Regular-season excludes playoff weeks
- [x] Playoff includes R12 qualification
- [x] `dotnet test` passes

## Files to touch

- `src/Swan.Services/Scoring/` (Standings)
- `tests/Swan.Services.Tests/` (standings tests)
