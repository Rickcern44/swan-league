---
id: SWAN-4.2
title: Seed and reset
status: done
parent: SWAN-4
---

# SWAN-4.2: Seed and reset

Intent: Idempotent seed of 32 NFL teams with divisions, 2025 division finishes and point values (PRD R3), one admin user, and a demo league (season, ~6 fake coaches with rosters, one sample week with games). Reset command drops and reseeds.

## Seed data

- **32 NFL teams** with divisions (AFC/NFC, East/West/South/North)
- **2025 division finishes** — final standings per division (points per finish 1–4, per R3)
- **Admin user** — seeded with demo credentials
- **Demo season** — one active season with seeded settings
- **Coach users** — ~6 fake coaches (for E2E demo)
- **Sample rosters** — one per coach
- **Sample week** — Week 1 with NFL games
- **Mailpit SMTP** — integration ready

## Acceptance criteria

- [x] Seed is idempotent (run twice → same row counts, no duplicates)
- [x] Seed runs on dev startup after migrate (or via command; documented)
- [x] Reset command documented (drops all and reseeds)
- [x] 2025 finishes data in one reviewable file (CSV or JSON)
- [x] Testcontainers test asserts: 32 teams, 1–4 finishes per division
- [x] No null division assignments
- [x] `dotnet test` passes
- [x] `aspire run` shows seeded data accessible

## Files to touch

- new `src/Swan.Data/Seed/` (seed data and builder)
- `src/Swan.Api/Program.cs` (call seed on startup in Development)
- `README.md` (document seed/reset commands)
- `tests/Swan.Services.Tests/` (add Testcontainers seed tests)

## Notes

Dev must source 2025 NFL division final standings. If unsure of any finish, flag in the handoff rather than guessing.
