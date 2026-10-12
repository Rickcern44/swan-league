---
id: SWAN-2.2
title: Core, Data, Services, ServiceDefaults skeletons
status: done
parent: SWAN-2
---

# SWAN-2.2: Core, Data, Services, ServiceDefaults skeletons

Intent: Create project skeletons following PRD §10.1. Swan.Core has one empty folder per bounded context plus IClock interface. Swan.Data has empty SwanDbContext + Npgsql reference. Swan.Services has context folders + Integrations. Swan.ServiceDefaults uses Aspire template.

## Bounded contexts (folders in Swan.Core)

- Picks
- Scoring
- Schedule
- League
- Money
- Communication
- Identity
- Platform

Plus: IClock interface (single file in Swan.Core root)

## Acceptance criteria

- [x] Project references match PRD §10.1 table exactly:
  - Core references nothing external (no EF, no ASP.NET)
  - Data → Core only
  - Services → Core, Data
  - Api → Services, ServiceDefaults
- [x] Swan.Api calls AddServiceDefaults and exposes /health endpoint
- [x] Solution builds with `dotnet build`
- [x] `dotnet format --verify-no-changes` passes

## Files to touch

- new `src/Swan.Core` with bounded context folders and IClock
- new `src/Swan.Data` with SwanDbContext
- new `src/Swan.Services` with context folders + Integrations
- new `src/Swan.ServiceDefaults`
- `Swan.slnx` (update project references)
