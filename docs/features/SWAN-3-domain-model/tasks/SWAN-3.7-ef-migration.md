---
id: SWAN-3.7
title: EF configuration + initial migration
status: todo
parent: SWAN-3
---

# SWAN-3.7: EF configuration + initial migration

Intent: Map all aggregates to Postgres via EF Core with proper configurations, owned types, and indexes. Create a single "InitialDomain" migration and ensure dev startup applies it.

## Mapping rules

- One `DbSet<T>` per aggregate root
- One DbContext configuration folder per context (Identity, League, etc.)
- Typed-ID value converters (e.g., `UserId` → string in DB)
- Owned types for value objects (e.g., `Money` owned by `LedgerItem`)
- Child entities owned by their aggregate root (no navigation from root to child, only loading)
- **No cross-aggregate navigations** (reference by ID only)
- Unique indexes:
  - `User.Email`
  - `Membership` (season + user unique)
  - `Notification` (recipient + event key unique)
  - `ScheduledJob` (key unique)
  - `PushSubscription` (endpoint unique)
- One Postgres schema per context (e.g., `public.identity`, `public.league`, etc.)

## Acceptance criteria

- [ ] `dotnet ef migrations add InitialDomain` produces no pending changes after migration creation
- [ ] Testcontainers test applies the migration and round-trips one aggregate per context
- [ ] `aspire run` brings up a fully migrated database
- [ ] Schemas are visible per context in `pg_namespace`
- [ ] No pending changes after running the migration
- [ ] Dev startup runs migrations automatically (either API in Development or an Aspire migration step; document choice in README)
- [ ] `dotnet test` passes; `dotnet format` clean

## Files to touch

- `src/Swan.Data/` (add EF configurations per context)
- `tests/Swan.Services.Tests/` (Testcontainers round-trip test)
- `apphost.cs` or `src/Swan.Api/Program.cs` (migration on startup)
- `README.md` (document migration strategy)
