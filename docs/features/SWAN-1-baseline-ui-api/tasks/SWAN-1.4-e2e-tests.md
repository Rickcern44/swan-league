---
id: SWAN-1.4
title: Playwright E2E baseline
status: done
parent: SWAN-1
---

# SWAN-1.4: Playwright E2E baseline

Intent: Playwright test project proving the UI renders data from the API end to end.

## Acceptance criteria

- [x] One E2E test that loads UI and asserts the API-sourced text
- [x] Single documented command runs it against a running stack
- [x] Test passes locally

## Files to touch

- new `tests/e2e` (or `src/web/e2e`)

## Command

With `aspire run` active, run `E2E_BASE_URL=<web URL from aspire describe> npm run test:e2e` from `src/web`.
