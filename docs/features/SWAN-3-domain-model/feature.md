---
id: SWAN-3
title: Domain model + base migration
status: done
parent: ~
---

# SWAN-3: Domain model + base migration

Reference: PRD §9 (contexts, aggregates, value objects, events, modeling rules, code mapping)

Intent: Implement all aggregates with structural invariants enforced and persisted. Aggregates exist with tested invariants; Swan.Data persists them with a single initial migration.

## In scope

- Domain types and invariants (enforced in root methods, unit-tested)
- Events as records (raised and collected, not dispatched)
- EF configuration and typed-ID converters
- Single "InitialDomain" migration

## Out of scope

- Policies (LockPolicy, PickVisibilityPolicy, ScoringPolicy, TiebreakPolicy, PayoutPolicy) — Phase 1
- Event dispatch and handlers — later
- Seed data, auth flows, endpoints, ImportBatch — later features
- Scoring math — Phase 1

## Conventions (all tasks)

- **TDD:** failing test first
- **Invariant violations:** return Result<T> failures (not exceptions)
- **Time:** passed as argument or IClock, never DateTime.Now
- **References:** aggregates reference others by typed ID only
- **Mutation:** private setters, no public collection mutation

## Tasks

- [[SWAN-3.1-kernel]] — Shared kernel + value objects
- [[SWAN-3.2-identity-league-platform]] — Identity, League, Platform aggregates
- [[SWAN-3.3-schedule]] — Schedule aggregates
- [[SWAN-3.4-picks-scoring]] — Picks and Scoring aggregates
- [[SWAN-3.5-money-communication]] — Money and Communication aggregates
- [[SWAN-3.6-arch-test]] — Context boundary architecture test
- [[SWAN-3.7-ef-migration]] — EF configuration + initial migration
- [[SWAN-3.8-test-coverage]] — Invariant test coverage + child round-trips
