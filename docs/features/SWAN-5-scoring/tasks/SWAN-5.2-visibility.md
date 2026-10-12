---
id: SWAN-5.2
title: PickVisibilityPolicy
status: done
parent: SWAN-5
---

# SWAN-5.2: PickVisibilityPolicy

Intent: Implement PRD R21 + A20: viewer sees another coach's locked picks only after the viewer's own picks are locked. Admins always see. Admin override flag per week (logged later).

## Design

- **CanSee(viewerPicks, targetPicks, isAdmin, adminOverride)** → bool
- Viewer sees target's team picks only if viewer's team picks are all locked OR admin
- Viewer sees target's dogs only if viewer's dogs are all locked OR admin
- Admin flag per week overrides (audit later)

## Acceptance criteria

- [x] Before own lock: cannot see others' locked picks
- [x] After own lock: can see others' locked picks
- [x] Partial lock (one early game done) does not reveal
- [x] Dogs independent from team picks
- [x] Admin always true
- [x] Coach never sees unlocked pick of another
- [x] `dotnet test` passes

## Files to touch

- `src/Swan.Core/Picks/` (PickVisibilityPolicy)
- `tests/Swan.Core.Tests/` (visibility tests)
