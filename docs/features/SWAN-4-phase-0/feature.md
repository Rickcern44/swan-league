---
id: SWAN-4
title: Phase 0 completion (auth, audit, seed, sign-in)
status: todo
parent: ~
---

# SWAN-4: Phase 0 completion

Reference: PRD §11 Phase 0, §10.3, §2 D7 / §3 / §8 N1, §9.3 User+LoginToken, §10 Auth+Security, §3 + A17 audit

Intent: Complete Phase 0 with authentication (magic-link sign-in), authorization (roles), audit logging, and seeded demo data. Exit criteria: one command starts the stack; admin and coach sign in with magic links caught in Mailpit; schema migrates and seeds cleanly; architecture tests pass.

## Out of scope

- Admin console UI beyond sign-in landing
- Wizard
- Notifications beyond magic-link and invite emails
- Rate-limit tuning beyond basic per-email/IP limit
- CI/CD workflows (user decision, deferred)

## Tasks

- [[SWAN-4.1-ef-hardening]] — EF design-time hardening
- [[SWAN-4.2-seed]] — Seed and reset
- [[SWAN-4.3-email]] — Email sender via Mailpit
- [[SWAN-4.4-magic-link]] — Magic-link sign-in API
- [[SWAN-4.5-roles]] — Roles and authorization
- [[SWAN-4.6-audit]] — Audit log
- [[SWAN-4.7-web-signin]] — Web sign-in flow + E2E

## Exit criteria

- [ ] `aspire run` brings up the full stack
- [ ] Admin and coach can sign in with magic links (caught in Mailpit)
- [ ] Schema migrates cleanly
- [ ] Demo data seeds idempotently
- [ ] Architecture tests pass
- [ ] Playwright E2E sign-in flow passes
