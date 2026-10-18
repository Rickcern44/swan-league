---
id: SWAN-2
title: Solution restructure to PRD architecture
status: done
parent: ~
---

# SWAN-2: Solution restructure to PRD architecture

Reference: PRD §9.7, §10.1–10.3

Intent: Reorganize the solution layout to match the architecture described in the PRD. Scaffold all projects and plumbing; no domain logic yet.

## User decisions (2026-10-12)

- **AppHost:** Keep single-file apphost.cs at repo root. Do NOT create Swan.AppHost project. Add Swan.ServiceDefaults project.
- **CI:** Deferred — no workflow file in this feature.
- **PWA:** Scaffold manifest + service worker (vite-plugin-pwa). Web push deferred to Phase 4.

## Tasks

- [SWAN-2.1](tasks/SWAN-2.1-rename-projects.md) — Rename existing projects
- [SWAN-2.2](tasks/SWAN-2.2-skeletons.md) — Core, Data, Services, ServiceDefaults
- [SWAN-2.3](tasks/SWAN-2.3-worker.md) — Worker host skeleton
- [SWAN-2.4](tasks/SWAN-2.4-apphost.md) — AppHost resources (Postgres, Mailpit, wiring)
- [SWAN-2.5](tasks/SWAN-2.5-tests.md) — Test project baselines
- [SWAN-2.6](tasks/SWAN-2.6-pwa.md) — Swan.Web PWA shell

## Out of scope

- Entities, migrations, seed data
- Magic-link auth, roles, audit log
- CI/CD workflows
- These belong to Phase 0 (will be planned separately)
