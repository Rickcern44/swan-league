---
id: SWAN-5.3
title: ScoringPolicy (regular season)
status: done
parent: SWAN-5
---

# SWAN-5.3: ScoringPolicy (regular season)

Intent: Implement PRD R3–R5, R9, R11, R15, R17: team points, Perfect Week, dog points, flags, collisions, missing picks, settings toggles.

## Rules

- **R3/R4** — team: win = point value; loss/tie = 0 (S5)
- **R5** — Perfect Week +3 (all 3 started, win; tie breaks it)
- **R9** — dog points = spread rounded up (O4)
- **R11** — Going Perf / 0fer flags
- **R15** — setting: dog to main pool (default off)
- **R17** — setting: team may be start + dog (default on)
- **§4** — collisions, missed picks (score 0), tie handling

## Acceptance criteria

- [x] One focused test per rule ID
- [x] WeekScore.Recompute builds ScoreLines with source + reference (S3)
- [x] Idempotent: recompute twice = same lines (S2)
- [x] Settings R15/R17 toggle the pools correctly
- [x] Collisions score normally
- [x] Missing/incomplete slots score 0
- [x] `dotnet test` passes

## Files to touch

- `src/Swan.Core/Scoring/` (ScoringPolicy regular)
- `tests/Swan.Core.Tests/` (scoring tests)
