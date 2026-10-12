---
id: SWAN-5.2
title: PickVisibilityPolicy
status: todo
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

- [ ] Before own lock: cannot see others' locked picks
- [ ] After own lock: can see others' locked picks
- [ ] Partial lock (one early game done) does not reveal
- [ ] Dogs independent from team picks
- [ ] Admin always true
- [ ] Coach never sees unlocked pick of another
- [ ] `dotnet test` passes

## Files to touch

- `src/Swan.Core/Picks/` (PickVisibilityPolicy)
- `tests/Swan.Core.Tests/` (visibility tests)
