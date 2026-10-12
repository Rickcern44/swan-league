---
id: SWAN-4.5
title: Roles and authorization
status: done
parent: SWAN-4
---

# SWAN-4.5: Roles and authorization

Intent: Implement role-based authorization. User.Role (Admin, Coach) is DB-assigned, never self-service. Policy-based authorization on every endpoint.

## Design

- **User.Role** — enum (Admin, Coach) in Swan.Core; DB-assigned at seed/invite time
- **Policies** — "Admin" and "Coach" policies in ASP.NET Core
- **Endpoint declarations** — every mapped endpoint declares an authorization policy or explicit [AllowAnonymous]
- **Architecture test** — reflect over endpoints; fail if any lacks authorization declaration

## Acceptance criteria

- [x] User.Role enum in Swan.Core
- [x] Policies "Admin" and "Coach" registered in Program.cs
- [x] Every mapped endpoint has an explicit Admin/Coach policy or AllowAnonymous metadata
- [ ] Tests:
  - [x] Coach gets 403 on admin-only endpoint
  - [x] Admin gets 200 on admin-only endpoint
  - [x] Architecture/reflection test fails if an endpoint lacks authorization
- [x] `dotnet test` passes

## Files to touch

- `src/Swan.Core/Identity/` (User.Role enum)
- `src/Swan.Api/Program.cs` (register policies)
- `src/Swan.Api/Endpoints/` (add [Authorize] attributes)
- `tests/Swan.Api.Tests/` (add role tests)
- `tests/Swan.Architecture.Tests/` (add endpoint authorization test)
