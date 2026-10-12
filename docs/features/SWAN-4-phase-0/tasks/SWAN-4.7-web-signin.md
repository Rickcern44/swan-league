---
id: SWAN-4.7
title: Web sign-in flow + E2E
status: done
parent: SWAN-4
---

# SWAN-4.7: Web sign-in flow + E2E

Intent: Implement sign-in UI in Swan.Web (sign-in page, email entry, "check your email" state, link redemption route, signed-in landing). Admin sees an "Admin" marker. End-to-end Playwright test proves the flow works.

## Pages/flows

- **Sign-in page** — email input, submit button
- **"Check your email" state** — after submission, shows confirmation
- **Link redemption route** — `/signin-callback?token=...` redeems and redirects to landing
- **Signed-in landing** — shows user name and role; admin sees "Admin" marker
- **Navigation** — sign-out link

## Acceptance criteria

- [x] Sign-in page with email input and submit (Vitest)
- [x] "Check your email" message after submission
- [x] `/signin-callback` route redeems token and redirects
- [x] Signed-in landing shows name + role
- [x] Admin landing shows "Admin" marker
- [x] Sign-out clears cookie and redirects
- [x] Playwright E2E:
  - [x] Request sign-in link for seeded admin
  - [x] Fetch link from Mailpit API
  - [x] Redeem link (click or programmatic)
  - [x] Assert signed in with admin role
  - [x] Repeat for seeded coach (assert coach role)
- [x] `npm lint`, `npm run format:check`, `npm test` (Vitest) pass
- [x] Playwright E2E passes

## Files to touch

- `src/Swan.Web/` (pages, components, API client)
- `src/Swan.Web/src/pages/` (sign-in, callback, landing)
- `tests/e2e/` (Playwright sign-in flow)

## Notes

E2E test uses the Mailpit API to fetch the sign-in link. Coordination: call `/api/request-sign-in-link`, wait a moment, fetch from Mailpit, then redeem.
