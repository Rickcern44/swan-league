---
id: SWAN-5.7
title: TiebreakPolicy
status: blocked
parent: SWAN-5
---

# SWAN-5.7: TiebreakPolicy

**STATUS: BLOCKED — Pending user rules document**

Intent: Implement per-pool tiebreaker rules (PRD §6.1 step 2, R18 color tiers). Ranks tied coaches when points equal.

## Blocker

Needs the league rules doc with per-pool tiebreakers (color tiers, etc.). Planner is asking the user (PRD §11 Phase 1 dependency).

## What this task will do (when unblocked)

- Read the user-provided rules doc
- Implement TiebreakPolicy per pool
- Resolve ties by applying tiebreaker order
- Update Standings to use TiebreakPolicy for ranking

## Files (to be determined)

- `src/Swan.Core/Scoring/` (TiebreakPolicy)
- `tests/Swan.Core.Tests/` (tiebreak tests)
