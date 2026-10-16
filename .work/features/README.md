# Feature and task handoffs

Create one directory per feature or independent task: `.work/features/<slug>/`.

- `plan.md` holds the feature scope and approval state. Use it for multi-task features.
- `tasks/001-<task>.md`, `tasks/002-<task>.md`, and so on hold ordered work units.
- A small independent task can have one task file without a full feature plan.

Use the templates in `.work/templates/`. Keep current status, decisions, and test results in the handoff files so another agent can resume without relying on chat history. Use Git history for commit IDs.
