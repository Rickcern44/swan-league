---
id: SWAN-4.1
title: EF design-time hardening
status: todo
parent: SWAN-4
---

# SWAN-4.1: EF design-time hardening

Intent: Harden EF tooling for design-time operations (migrations, scripts). Factory exists and works; this task hardens the connection string, pins tool versions, documents commands, and adds verification tests.

## Current state

- `src/Swan.Data/SwanDbContextFactory.cs` exists (planner verified)
- Migrations list/script work
- `dotnet-ef` is not pinned (global 10.0.10 vs EF packages 10.0.3)
- Connection string is hard-coded
- Commands are undocumented

## Acceptance criteria

- [ ] Factory reads connection string from env var `SWAN_DESIGN_CONNECTION` or CLI arg `-- --connection`, with fallback to local default
- [ ] `.config/dotnet-tools.json` pins `dotnet-ef` to match EF package version
- [ ] `dotnet tool restore` works
- [ ] README documents: `dotnet ef migrations add`, `dotnet ef migrations list`, `dotnet ef migrations script`, `dotnet ef has-pending-model-changes` (all with `--project src/Swan.Data`)
- [ ] Unit test constructs context via factory without a DB; asserts provider is Npgsql
- [ ] Test or script asserts `has-pending-model-changes` is clean after migration
- [ ] `dotnet test` passes

## Files to touch

- `src/Swan.Data/SwanDbContextFactory.cs`
- `.config/dotnet-tools.json`
- `README.md`
- `tests/Swan.Services.Tests/` (add factory and has-pending tests)
