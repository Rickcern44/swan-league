---
id: SWAN-4.7
title: Web sign-in flow + E2E
status: todo
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

- [ ] Sign-in page with email input and submit (Vitest)
- [ ] "Check your email" message after submission
- [ ] `/signin-callback` route redeems token and redirects
- [ ] Signed-in landing shows name + role
- [ ] Admin landing shows "Admin" marker
- [ ] Sign-out clears cookie and redirects
- [ ] Playwright E2E:
  - [ ] Request sign-in link for seeded admin
  - [ ] Fetch link from Mailpit API
  - [ ] Redeem link (click or programmatic)
  - [ ] Assert signed in with admin role
  - [ ] Repeat for seeded coach (assert coach role)
- [ ] `npm lint`, `npm run format:check`, `npm test` (Vitest) pass
- [ ] Playwright E2E passes

## Files to touch

- `src/Swan.Web/` (pages, components, API client)
- `src/Swan.Web/src/pages/` (sign-in, callback, landing)
- `tests/e2e/` (Playwright sign-in flow)

## Notes

E2E test uses the Mailpit API to fetch the sign-in link. Coordination: call `/api/request-sign-in-link`, wait a moment, fetch from Mailpit, then redeem.
