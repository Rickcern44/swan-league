---
id: SWAN-3.1
title: Shared kernel + value objects
status: done
parent: SWAN-3
---

# SWAN-3.1: Shared kernel + value objects

Intent: Build core abstractions and value objects used across all aggregates.

## Types to implement

**Bases:**
- `AggregateRoot` — base with domain event collection (no dispatch yet)
- `Entity` — base for child entities
- `Result<T>` / `Error` — for invariant violations (no exceptions)

**Strongly typed IDs (one per aggregate root):**
- `UserId`, `SeasonId`, `MembershipId`, `AuditEntryId`, `ScheduledJobId`
- `TeamId`, `SeasonTeamId`, `WeekId`, `GameId`
- `RosterId`, `BallotId`, `WeekScoreId`
- `LedgerId`, `PoolSettlementId`, `AnnouncementId`, `NotificationId`, `PushSubscriptionId`

**Value Objects (PRD §9.4):**
- `Money` — positive amount, currency ISO code
- `PointValue` — 1–4 for dog point spreads
- `Spread` — signed decimal (home team perspective)
- `DogPoints` — decimal, rounds up (+6.5 → 7)
- `Kickoff` — datetime in UTC
- `GameResult` — home/away scores, derives winner/tie (no separate field)
- `Email` — validates format
- `Audience` — enum for notification recipients

## Acceptance criteria

- [x] Each VO rejects invalid input with unit tests (e.g., negative Money, PointValue outside 1–4, malformed Email)
- [x] DogPoints rounding tested (+6.5 → 7)
- [x] GameResult derives winner/tie correctly (test both)
- [x] Strongly typed IDs are distinct at compile time (e.g., can't assign `UserId` to `TeamId`)
- [x] AggregateRoot collects events (not dispatched)
- [x] `dotnet test` passes; `dotnet format --verify-no-changes` clean

## Files to touch

- new `src/Swan.Core/Common/` (or similar) for shared types
- new `tests/Swan.Core.Tests/` (or extend existing)
