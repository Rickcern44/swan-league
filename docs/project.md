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
| 2. Admin week workflow, scores import, simulation | In progress. Admin week lifecycle API (`/api/admin/weeks/{weekId}`) is built, and per-game line history is persisted so a line can be added and later approved via `lines/approve-latest`. Admins can record manual game results on a locked week, finalize it, and recompute `WeekScore`s (`/games/{gameId}/result`, `/finalize`, `/recompute`; regular-season weeks only, default scoring settings because none are persisted). `POST /api/admin/seasons/{seasonId}/weeks/recompute` rescores every Final regular-season week of a season on demand (used to correct weeks scored before dog points became Dog-pool-only). A draft week's dog list can be generated from approved lines (`/dog-options/generate`; spread is home-perspective, so +x makes the home team the dog, -x the away team) or replaced by an admin (`PUT /dog-options`). An admin can create one blank Setup season (`POST /api/admin/seasons`, `GET /api/admin/seasons/setup`): PRD default $100 buy-in split Main 60 / Dog 20 / Regular Season 10 / Playoff 10, three double-dog weeks, Sunday 1:00 PM ET deadline; year, name and simulation flag come from the caller and can be revised while the season is in Setup (`PUT /api/admin/seasons/setup`). The buy-in and the four fixed pool stakes can be revised too (`PUT /api/admin/seasons/setup/money`): all USD and positive, stakes must add up to the buy-in, pool names are fixed, and each real change writes a `season.setup.money.updated` audit entry (identical replays write none). Edits take a row lock and run in the retrying execution strategy so they cannot race activation. The weekly team-pick deadline (Eastern `TimeOnly`, JSON `"HH:mm:ss"`, body `{"teamPickDeadlineEastern": "11:30:00"}`) can be revised too (`PUT /api/admin/seasons/setup/deadline`; missing, null or malformed is 400): it returns from `GET /api/admin/seasons/setup`, feeds `LockPolicy.WeekDeadline`, and each real change writes a `season.setup.deadline.updated` audit entry (replays write none). Double-dog weeks are deliberately not editable yet: the code holds only an aggregate count (`DoubleDogWeeks`) and no per-Week double-dog state, while the PRD needs actual week numbers (Week 2 plus schedule-based suggestions), which depends on the schedule/Week-model slice. The admin landing page now shows this as a three-page Season setup wizard (start a season, then Basics, Money and Weekly deadline, each with Back and Save & continue and saving through its endpoint above). The season stores a Setup-only resume step (`SetupStep`: Basics, Money, WeeklyDeadline, TeamsAndPoints; new and pre-existing seasons open at Basics), returned on the Setup summary and saved with `PUT /api/admin/seasons/setup/step` (`{"step": 1}`; unknown or missing step is 400, replays write no audit, real changes write `season.setup.step.updated`). A successful save records the next page as the resume point without ever moving it backwards. The fourth page, Teams and point values, lists the 32 canonical teams by division (`GET /api/admin/seasons/setup/teams`) and saves a prior-season finish for each (`PUT /api/admin/seasons/setup/teams`, body `{"teams":[{"teamId":…,"finishPosition":1}]}`): exactly the known teams once each, finishes 1 to 4, one team per finish in every division, with the point value derived as the finish (no point-value input). Rows are `SeasonTeam` rows of the Setup season only, replaced as one set under the same row lock, with `season.setup.teams.updated` audited on real changes only; the database enforces one row per (season, team) and per (season, division, finish) (the migration refuses to run if existing rows already break either rule). Saving records Schedule import as the resume point; Schedule import and Weeks and dogs are listed as coming next but are not pages yet. Fine amount, payout splits, double-dog week numbers and playoff settings (the rest of the wizard) remain. Only one Setup season can exist, enforced by a partial unique index (`ux_seasons_single_setup`); the migration refuses to run if duplicate Setup seasons already exist and never deletes or merges them. Scores import and simulation remain. | — |
| 3. Coach picks | After Phase 2. | — |
| 4. Notifications | After Phase 3. | — |
| 5–9 (milestones 2 and 3) | Not started. | — |

## Open items

- **Phase 0 acceptance**: run `aspire run` and the Playwright sign-in flow, and record the evidence in [[SWAN-4-phase-0]].
