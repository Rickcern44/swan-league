---
id: project
title: Swan League Tracker
prefix: SWAN
---

# Swan League Tracker

**Product source of truth:** [docs/reference/Swan League App — Product Requirements.md](reference/Swan%20League%20App%20—%20Product%20Requirements.md). Scope and phasing live there (§11); this page only tracks delivery against it.

**ID scheme:** features are `SWAN-n` (our work). PRD requirements are `R`, `C`, `A`, `O`, `S`, `N`, `M` + number (see the PRD).

**Feature records:** `docs/features/` holds implementation history. See [features/README.md](features/README.md).

## Where we are

Phases 0 and 1 are implemented in code. Phase 2 (admin week workflow and simulation) is next, then Phase 3 (coach picks) and Phase 4 (notifications).

Evidence levels used below:

- **Implemented**: code and tests exist. `dotnet test Swan.slnx --no-restore` passed 148 tests (Core 65, Services 16, Architecture 62, API 4, Worker 1).
- **Accepted end to end**: the full Aspire stack and Playwright flows have been run against the exit criteria. Not yet done for any phase: no Aspire-stack or Playwright run has been recorded.

## Delivery by PRD §11 phase

| Phase | Status | Records |
| --- | --- | --- |
| 0. Foundations | Implemented; end-to-end acceptance pending. CI is deferred by a prior project decision. | [[SWAN-1-baseline-ui-api]] (superseded), [[SWAN-2-solution-restructure]], [[SWAN-3-domain-model]], [[SWAN-4-phase-0]] |
| 1. Scoring core | Implemented (R1–R14 policies, fixtures, standings). Tied coaches share a rank under competition ranking (`1, 1, 1, 4`); no secondary tiebreak is applied. | [[SWAN-5-scoring]] |
| 1b. Hosting research | Not started. | — |
| 2. Admin week workflow, scores import, simulation | In progress. Admin week lifecycle API (`/api/admin/weeks/{weekId}`) is built, and per-game line history is persisted so a line can be added and later approved via `lines/approve-latest`. Scores import and simulation remain. | — |
| 3. Coach picks | After Phase 2. | — |
| 4. Notifications | After Phase 3. | — |
| 5–9 (milestones 2 and 3) | Not started. | — |

## Open items

- **Phase 0 acceptance**: run `aspire run` and the Playwright sign-in flow, and record the evidence in [[SWAN-4-phase-0]].
