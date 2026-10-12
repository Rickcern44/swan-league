---
id: SWAN-1.6
title: Ignore build artifacts
status: done
parent: SWAN-1
---

# SWAN-1.6: Ignore build artifacts

Intent: Git status clean after build/test/e2e.

## Acceptance criteria

- [x] `.gitignore` covers `bin/`, `obj/`, `node_modules/`, `dist/`, `test-results/`, `playwright-report/`
- [x] After `dotnet test` + `npm test` + e2e, `git status` shows nothing untracked from builds
- [x] Nothing already tracked is removed

## Files to touch

- `.gitignore`
