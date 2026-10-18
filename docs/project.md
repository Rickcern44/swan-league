---
id: project
title: Swan League Tracker
prefix: SWAN
---

# Swan League Tracker

The [product requirements](reference/Swan%20League%20App%20%E2%80%94%20Product%20Requirements.md) are the source of truth for scope and delivery phases. This page is a concise delivery summary; detailed implementation history lives in [features](features/README.md).

## Current state

The application has a working .NET API, React/Vite web client, Aspire AppHost, PostgreSQL persistence, authentication, seeded development data, scoring, season setup, roster drafting, and coach pick flows.

| Area | Status | Evidence |
| --- | --- | --- |
| Foundations | Implemented | [SWAN-1](features/SWAN-1-baseline-ui-api/feature.md), [SWAN-2](features/SWAN-2-solution-restructure/feature.md), [SWAN-3](features/SWAN-3-domain-model/feature.md), and [SWAN-4](features/SWAN-4-phase-0/feature.md) |
| Scoring core | Implemented | [SWAN-5](features/SWAN-5-scoring/feature.md) |
| Admin week workflow and simulation | In progress | Lifecycle and setup flows are implemented; provider-backed scores import and simulation remain. |
| Coach picks | In progress | Roster drafting and the coach pick desk are implemented; visibility, reminders, fines, and standings UI remain. |
| Notifications and later milestones | Not started | See PRD §11. |

## Evidence levels

- **Implemented** means code and automated tests exist.
- **Accepted end to end** means the Aspire stack and relevant Playwright flows have been run against the exit criteria.

End-to-end acceptance has not yet been recorded for the foundations. The next verification milestone is to run the full stack and the sign-in Playwright flow, then record the result in [SWAN-4](features/SWAN-4-phase-0/feature.md).

## Documentation map

- [README](../README.md): local setup and verification commands.
- [Product requirements](reference/Swan%20League%20App%20%E2%80%94%20Product%20Requirements.md): product scope, decisions, architecture, and phases.
- [Feature history](features/README.md): completed implementation records and supporting task notes.
