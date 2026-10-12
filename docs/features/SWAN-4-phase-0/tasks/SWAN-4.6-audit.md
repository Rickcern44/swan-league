---
id: SWAN-4.6
title: Audit log
status: todo
parent: SWAN-4
---

# SWAN-4.6: Audit log

Intent: Append-only audit log for admin actions via AuditEntry (already in SWAN-3). Admin-only GET /api/admin/audit endpoint with basic filters (actor, action, date). No update/delete path exists (architecture-enforced).

## Design

- **AuditEntry** — immutable, created in SWAN-3 (no mutators)
- **AuditService** (or similar) — append-only helper in Services for logging actions
- **GET /api/admin/audit** — admin-only, returns filtered AuditEntry list (filter by actor, action, date)
- **Architecture test** — no UPDATE or DELETE on AuditEntry

## Acceptance criteria

- [ ] AuditEntry writes append actor, time, before/after values, reason on admin actions
- [ ] AuditService helper in Swan.Services logs actions
- [ ] GET /api/admin/audit endpoint (admin-only) with filters:
  - [ ] actor (UserId)
  - [ ] action (enum or string)
  - [ ] date range
- [ ] Tests:
  - [ ] Admin action logs to AuditEntry
  - [ ] Coach gets 403 on audit endpoint
  - [ ] Architecture/reflection test: no AuditEntry.Update or .Delete calls exist
- [ ] `dotnet test` passes

## Files to touch

- `src/Swan.Services/Platform/` (AuditService)
- `src/Swan.Api/Endpoints/Admin/` (GET /api/admin/audit)
- `tests/Swan.Services.Tests/` (audit logging tests)
- `tests/Swan.Architecture.Tests/` (no AuditEntry mutations)
