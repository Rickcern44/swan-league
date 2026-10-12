---
id: SWAN-5.5
title: Scoring fixture suite
status: done
parent: SWAN-5
---

# SWAN-5.5: Scoring fixture suite

Intent: Hand-built sample weeks (JSON fixtures) + expected per-coach totals. Run through real policies (PRD §10 Testing). No code changes needed to add fixtures.

## Fixtures to include

- Normal week
- Tie, bye team on roster
- Collision
- Double-dog week (two dogs, distinct games)
- Perfect Week
- Going Perf, 0fer
- Incomplete ballot
- R15 on vs off
- R17 on vs off

## Acceptance criteria

- [x] Fixture data files (JSON) with input week + expected scores
- [x] Test harness runs fixtures through policies
- [x] All fixtures pass
- [x] Adding a new fixture needs no code change
- [x] `dotnet test` passes

## Files to touch

- `tests/Swan.Core.Tests/Fixtures/` (fixture data)
- `tests/Swan.Core.Tests/` (fixture test harness)
