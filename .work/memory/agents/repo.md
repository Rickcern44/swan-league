# Repository knowledge

Record durable, verified facts with a source path or command and date. Update stale notes when the code changes. Do not store secrets or speculation.

## Conventions

- 2026-10-16 — Use TDD, Conventional Commits, and run `dotnet format` plus `dotnet format --verify-no-changes` before commits. For UI changes, run `npm run format`, `npm run format:check`, and `npm run lint` in `src/Swan.Web`. Source: `AGENTS.md`.
- 2026-10-16 — UI behavior needs Playwright E2E against a running Aspire stack. Use a dedicated test database; the E2E test mutates setup data. Sources: `AGENTS.md`, `README.md`.

## Architecture

- 2026-10-16 — The repository contains an ASP.NET Core API, React/Vite UI, and .NET Aspire AppHost. Sources: `README.md`, `apphost.cs`, `src/Swan.Web/package.json`.
- 2026-10-16 — The .NET solution is `Swan.slnx`, with projects under `src/` and tests under `tests/`. Source: `Swan.slnx`.

## Commands and checks

- 2026-10-16 — `dotnet test Swan.slnx` runs the .NET tests. Source: `README.md`.
- 2026-10-16 — UI scripts include `npm test`, `npm run build`, `npm run lint`, and `npm run test:e2e` in `src/Swan.Web`. Source: `src/Swan.Web/package.json`.

## Known pitfalls

- 2026-10-16 — E2E tests may alter Setup season data; follow the dedicated test database and cleanup guidance before running them. Source: `README.md`.
