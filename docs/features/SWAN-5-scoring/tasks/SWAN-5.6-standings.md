---
id: SWAN-5.6
title: Standings read models
status: done
parent: SWAN-5
---

# SWAN-5.6: Standings read models

Intent: Standings queries for Main, Dog, Regular Season, Playoff pools (PRD C10): rank, points, wins, current week and season-to-date, from stored WeekScores. Ties marked "unbroken" (TiebreakPolicy pending).

## Pools

- Main pool
- Dog pool
- Regular season pool (excludes playoffs)
- Playoff pool (R12 qualification + R13 multipliers)

## Acceptance criteria

- [x] Read model computes rank, points, wins per pool
- [x] Ties share a rank, marked "unbroken"
- [x] Testcontainers test: seed weeks, assert totals and ranks
- [x] R15 setting changes main-pool totals
- [x] Regular-season excludes playoff weeks
- [x] Playoff includes R12 qualification
- [x] `dotnet test` passes

## Files to touch

- `src/Swan.Services/Scoring/` (Standings)
- `tests/Swan.Services.Tests/` (standings tests)
