---
id: SWAN-3.5
title: Money and Communication aggregates
status: done
parent: SWAN-3
---

# SWAN-3.5: Money and Communication aggregates

Intent: Implement Money and Communication contexts for fines, payouts, announcements, and notifications.

## Aggregates

**Money context:**
- `Ledger` — season, list of LedgerItems (fines, payouts, user-initiated transfers)
- `LedgerItem` — amount, status (Pending → Confirmed/Waived → Paid), memo, user references (payer/payee/admin)
- `PoolSettlement` — season, list of Placements (member + position + payout amount), settlement once flag
- `Placing` — member ID, finish position, payout amount
- Events: `FineAssessed`, `PoolSettled`

**Communication context:**
- `Announcement` — content, schedule (future datetime), sent flag (one-time send)
- `Notification` — recipient ID, event key (unique per recipient+event), content, read flag
- `NotificationPreferences` — member ID, category mute flags (some essential categories cannot be muted per PRD §8 M10)
- `PushSubscription` — member ID, endpoint, keys (push service subscription)

## Acceptance criteria

- [x] Ledger: item status machine Pending → Confirmed/Waived → Paid (never back, only forward)
- [x] LedgerItem amounts always positive
- [x] PoolSettlement: settles once (not twice); payouts sum to the pool pot
- [x] Announcement: schedule must be future; sent once (idempotent send)
- [x] Notification: unique per recipient+event key (will use DB unique index later)
- [x] NotificationPreferences: essential categories (e.g., member invitations) cannot be muted
- [x] PushSubscription stores endpoint and encryption keys
- [x] Events `FineAssessed` and `PoolSettled` raised and collected
- [x] `dotnet test` passes; `dotnet format` clean

## Files to touch

- new `src/Swan.Core/Money/`
- new `src/Swan.Core/Communication/`
- `tests/Swan.Core.Tests/` (add context tests)
