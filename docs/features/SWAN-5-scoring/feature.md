---
id: SWAN-5
title: Phase 1 scoring core
status: blocked
parent: ~
---

# SWAN-5: Phase 1 scoring core

Reference: PRD §11 Phase 1, §4 (lock behavior, missed picks, collisions), §7 (scoring rules), §9.4 policies, §10.3 "Build Core first"

Intent: Implement scoring engine for regular season and playoffs. All logic is pure Core code (no EF/HTTP), TDD, time via IClock or passed-in instants.

## Exit criteria

- Fixture suite passes (includes ties, byes, collisions, double-dog weeks, Perfect Week)
- All scoring rules (R1–R21) covered by tests
- Standings read models working for all four pools

## Out of scope

- Endpoints, UI, jobs
- Fines ledger writes (R10 only flags incomplete)
- TiebreakPolicy ranking rules (waiting on user)
- Payouts (Phase 8)

## Tasks

- Housekeeping: delete stray folder, add gitignore pattern
- [[SWAN-5.1-lock-policy]] — LockPolicy (compute lock times per pick)
- [[SWAN-5.2-visibility]] — PickVisibilityPolicy (control what coaches see)
- [[SWAN-5.3-scoring-regular]] — ScoringPolicy (regular season)
- [[SWAN-5.4-scoring-playoffs]] — ScoringPolicy (playoffs)
- [[SWAN-5.5-fixtures]] — Scoring fixture suite
- [[SWAN-5.6-standings]] — Standings read models (four pools)
- [[SWAN-5.7-tiebreak]] — TiebreakPolicy (BLOCKED pending user rules doc)
