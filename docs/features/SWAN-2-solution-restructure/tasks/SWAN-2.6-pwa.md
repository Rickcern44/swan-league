---
id: SWAN-2.6
title: Swan.Web PWA shell
status: todo
parent: SWAN-2
---

# SWAN-2.6: Swan.Web PWA shell

Intent: Add PWA scaffolding with vite-plugin-pwa. Create manifest (name "Swan League", placeholder icons) + service worker registration. Keep the API-response page as a smoke test for connectivity.

## Acceptance criteria

- [ ] `npm lint` passes
- [ ] `npm run format:check` passes
- [ ] `npm test` (Vitest) passes
- [ ] Playwright e2e passes
- [ ] DevTools and Lighthouse show an installable manifest
- [ ] Active service worker registered in preview build
- [ ] Mobile-first base layout in place

## Files to touch

- `src/Swan.Web` (update vite config, add manifest, register SW)
- `src/Swan.Web/public/` (add manifest.json, placeholder icons)
- `src/Swan.Web/src/` (add SW registration)

## Skills

- `.agents/skills/aspire-orchestration` (for PWA integration with Aspire)

## Notes

No web push code yet — that belongs to Phase 4.
