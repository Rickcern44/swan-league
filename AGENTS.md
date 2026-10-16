# Swan League Tracker: Codex workflow

## Development rules

- Read the relevant code and existing conventions before changing behavior. Keep each change focused and protect unrelated work.
- Use TDD: write a failing test before implementing each behavior. Add or update tests for every behavior change.
- Before each commit, run `dotnet format Swan.slnx` and `dotnet format Swan.slnx --verify-no-changes`. For UI changes, also run `npm run format`, `npm run format:check`, and `npm run lint` in `src/Swan.Web`.
- Test UI behavior end to end with Playwright (`npm run test:e2e` in `src/Swan.Web`) against a running Aspire stack and dedicated test database. Follow the cleanup cautions in `README.md`.
- Use Conventional Commits and small commits. Name new branches with the commit type and a short slug, such as `feat/season-setup` or `fix/login-timeout`.
- Report the commands run and their results. Never claim a check passed unless it ran. Keep secrets and private data out of commits and reports.

## Flow

The main Codex chat is the orchestrator. The `planner` and `developer` are custom agents in `.codex/agents/`. Read relevant handoffs under `.work/features/` and verified repository notes in `.work/memory/agents/repo.md`.

1. For a large feature with multiple tasks, start a fresh `feat/<slug>` branch from `main`. Delegate to `planner` to create a draft plan and numbered task handoffs in `.work/features/<slug>/`. Review the plan with the user and wait for explicit approval before implementation. Commit the approved handoff as a planning unit.
2. For a small independent task, skip the planner. Start a fresh branch from `main` and delegate a bounded task with acceptance criteria to `developer`.
3. For an approved feature, delegate one task at a time to `developer`. Review each result and test evidence. Commit each completed task, including its `.work` handoff and relevant memory updates. Continue through approved tasks without asking at each handoff; pause for material scope changes or decisions only the user can make.
4. After final checks, follow the repo's PR and CI rules to merge into `main`; if there is no PR workflow, merge locally. Never force a merge over unrelated changes. Start the next unit of work from updated `main`.

The user has explicitly authorized Flow in this repo, after approving a feature plan and required checks pass, to push that feature branch, open the required PR, and merge it into `main` without another routine approval. Pause if scope changes materially, required checks fail, or the merge conflicts. For independent tasks, get separate authorization before pushing or opening a PR.
