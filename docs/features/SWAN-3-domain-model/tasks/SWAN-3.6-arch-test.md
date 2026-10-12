---
id: SWAN-3.6
title: Context boundary architecture test
status: todo
parent: SWAN-3
---

# SWAN-3.6: Context boundary architecture test

Intent: Enforce PRD §9.7 context boundary rules: one context's Core folder may reference another context only through ID types and events.

## Acceptance criteria

- [ ] Architecture test using NetArchTest (or similar) enforces: a context may not reference another context's aggregate roots or value objects directly
- [ ] Test allows references to typed IDs and domain events from other contexts
- [ ] Test fails if e.g. `Picks.Ballot` directly references `Schedule.Week` (not just by ID)
- [ ] Test passes on current code
- [ ] Test has a documented failing case (add a forbidden reference, run the test, show it fails, then remove; note it in the handoff)
- [ ] `dotnet test` passes

## Files to touch

- `tests/Swan.Architecture.Tests/` (add or extend context boundary tests)
