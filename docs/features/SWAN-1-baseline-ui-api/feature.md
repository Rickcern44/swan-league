---
id: SWAN-1
title: Runnable baseline (UI + API)
status: todo
parent: ~
---

# SWAN-1: Runnable baseline (UI + API)

Intent: `aspire run` brings up an API and a UI that talks to it, with tests. No domain features yet.

Stack:
- .NET 10 + Aspire 13.5 file-based AppHost (apphost.cs)
- ASP.NET Core Minimal API
- React + Vite + TypeScript UI
- xUnit for API tests
- Vitest for UI unit tests
- Playwright for E2E tests

## Tasks

- [[SWAN-1.1-agent-rules]] — Agent working rules
- [[SWAN-1.2-api-service]] — API service + AppHost
- [[SWAN-1.3-ui-app]] — UI app + AppHost
- [[SWAN-1.4-e2e-tests]] — Playwright E2E baseline
- [[SWAN-1.5-readme]] — Developer README
- [[SWAN-1.6-gitignore]] — Ignore build artifacts
