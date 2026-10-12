---
id: SWAN-2.5
title: Test project baselines
status: done
parent: SWAN-2
---

# SWAN-2.5: Test project baselines

Intent: Create test projects with baseline tests and real architecture enforcement.

Projects:
- **Swan.Core.Tests** (xUnit): one placeholder test
- **Swan.Services.Tests** (xUnit + Testcontainers): SwanDbContext connects + EnsureCreated works
- **Swan.Architecture.Tests** (xUnit + NetArchTest): enforce PRD §10.1 rules
  - Core references nothing outside itself (no EF, no ASP.NET)
  - Data does not reference Services
  - Api endpoints do not reference Data
  - Include at least one test to verify these rules (show it failing when a forbidden reference is temporarily added, but don't commit the failure)

## Acceptance criteria

- [x] `dotnet test` runs all four .NET test projects and passes
- [x] Swan.Core.Tests has one placeholder test (passes)
- [x] Swan.Services.Tests connects to Postgres via Testcontainers and calls EnsureCreated (passes; Docker required)
- [x] Swan.Architecture.Tests includes real failing case (documented in handoff, not committed)
- [x] README notes Docker requirement for Services.Tests

## Files to touch

- new `tests/Swan.Core.Tests`
- new `tests/Swan.Services.Tests`
- new `tests/Swan.Architecture.Tests`
- new `tests/Swan.Worker.Tests`
- `README.md` (add Docker requirement note)

## Notes

The architecture test should have one test that fails when you add a forbidden reference. Show this in a comment or documentation, then remove it before committing.

## Verification note

Temporarily adding an ASP.NET Core framework reference and a Core type using `HttpContext` made `CoreDoesNotDependOnFrameworks("Microsoft.AspNetCore")` fail. The temporary dependency was removed before commit.
