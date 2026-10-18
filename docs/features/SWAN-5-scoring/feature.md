---
id: SWAN-5
title: Phase 1 scoring core
status: implemented
parent: ~
---

# SWAN-5: Phase 1 scoring core

Reference: PRD §11 Phase 1, §4 (lock behavior, missed picks, collisions), §7 (scoring rules), §9.4 policies, §10.3 "Build Core first"

Intent: Implement scoring engine for regular season and playoffs. All logic is pure Core code (no EF/HTTP), TDD, time via IClock or passed-in instants.

## Exit criteria

Phase 1 scope per PRD §11 is R1–R14, the fixture suite, and standings for all four pools.

- Fixture suite passes (includes ties, byes, collisions, double-dog weeks, Perfect Week)
- Scoring rules R1–R14 covered by tests
- Standings read models working for all four pools

## Status

R1–R14 policies, fixtures and standings are implemented. Tied coaches use competition ranking (`1, 1, 1, 4`) with no secondary tiebreak ([SWAN-5.7](tasks/SWAN-5.7-tiebreak.md)). Not part of this claim:

- **Adjacent policies** implemented and tested but not Phase 1 exit criteria: R20 lock times ([SWAN-5.1](tasks/SWAN-5.1-lock-policy.md)) and R21 pick visibility ([SWAN-5.2](tasks/SWAN-5.2-visibility.md)).
- **Settings** R17 is covered by the scoring tests, and R15 (dog points count only in the Dog pool, fixed) by the standings tests ([SWAN-5.3](tasks/SWAN-5.3-scoring-regular.md), [SWAN-5.6](tasks/SWAN-5.6-standings.md)).
- **R16** core invariant is represented in the domain (`Ballot.AddDog` allows the same team as a dog across weeks and rejects a repeated team or game within one ballot); coach pick-flow validation is Phase 3.
- **Not implemented here:** R18 color-tier configuration and R19 (records, Phase 8).

## Out of scope

- Endpoints, UI, jobs
- Fines ledger writes (R10 only flags incomplete)
- Secondary tiebreak rules (owner chose competition ranking, no tiebreak)
- Payouts (Phase 8)

## Tasks

- [SWAN-5.0](tasks/SWAN-5.0-housekeeping.md) — Housekeeping: delete stray folder, add gitignore pattern
- [SWAN-5.1](tasks/SWAN-5.1-lock-policy.md) — LockPolicy (compute lock times per pick)
- [SWAN-5.2](tasks/SWAN-5.2-visibility.md) — PickVisibilityPolicy (control what coaches see)
- [SWAN-5.3](tasks/SWAN-5.3-scoring-regular.md) — ScoringPolicy (regular season)
- [SWAN-5.4](tasks/SWAN-5.4-scoring-playoffs.md) — ScoringPolicy (playoffs)
- [SWAN-5.5](tasks/SWAN-5.5-fixtures.md) — Scoring fixture suite
- [SWAN-5.6](tasks/SWAN-5.6-standings.md) — Standings read models (four pools)
- [SWAN-5.7](tasks/SWAN-5.7-tiebreak.md) — Competition ranking for tied standings
