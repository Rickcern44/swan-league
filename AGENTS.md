# Swan League Tracker: Codex workflow

## Development rules

- Read the relevant code and existing conventions before changing behavior. Keep each change focused and protect unrelated work.
- Use TDD: write a failing test before implementing each behavior. Add or update tests for every behavior change.
- Before each commit, run `dotnet format Swan.slnx` and `dotnet format Swan.slnx --verify-no-changes`. For UI changes, also run `npm run format`, `npm run format:check`, and `npm run lint` in `src/Swan.Web`.
- Test UI behavior end to end with Playwright (`npm run test:e2e` in `src/Swan.Web`) against a running Aspire stack and dedicated test database. Follow the cleanup cautions in `README.md`.
- Use Conventional Commits and small commits. Name new branches with the commit type and a short slug, such as `feat/season-setup` or `fix/login-timeout`.
- Report the commands run and their results. Never claim a check passed unless it ran. Keep secrets and private data out of commits and reports.

## Flow

- For work that changes behavior, use a fresh branch from `main` and agree acceptance criteria before implementation. For a large feature, agree a written plan before coding.
- Keep commits small and review the diff and test evidence before merging. Never force a merge over unrelated changes.
- Push, open pull requests, and merge only when the user has authorized those actions. Pause for material scope changes, failed required checks, or merge conflicts.
