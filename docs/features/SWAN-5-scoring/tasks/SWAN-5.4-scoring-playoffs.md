---
id: SWAN-5.4
title: ScoringPolicy (playoffs)
status: todo
parent: SWAN-5
---

# SWAN-5.4: ScoringPolicy (playoffs)

Intent: Implement PRD R12–R14: qualification bonus, round multipliers, #1-seed bye compensation.

## Rules

- **R12** — qualification +3 (playoff pool only)
- **R13** — season point value × round multiplier (WC ×2, DIV ×4, CONF ×6, SB ×8)
- **R14 / §2** — #1-seed bye compensation 10 points when that team wins conference
- **§9.6** — playoff weeks have picks-required flag; up to 3 roster teams still alive

## Acceptance criteria

- [ ] One focused test per rule ID
- [ ] Hand-calculated playoff fixture matches
- [ ] Multipliers applied per round
- [ ] Bye compensation awarded correctly
- [ ] `dotnet test` passes

## Files to touch

- `src/Swan.Core/Scoring/` (ScoringPolicy playoffs)
- `tests/Swan.Core.Tests/` (playoff scoring tests)
