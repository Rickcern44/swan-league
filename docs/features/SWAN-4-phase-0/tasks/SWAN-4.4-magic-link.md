---
id: SWAN-4.4
title: Magic-link sign-in API
status: todo
parent: SWAN-4
---

# SWAN-4.4: Magic-link sign-in API

Intent: Implement magic-link sign-in flow (PRD C1 / §3): RequestSignInLink and RedeemSignInLink commands in Identity context; tokens hashed at rest, single-use (C1), 15-min expiry; session cookie 30 days; basic rate limit per email and IP (§10 Auth); safe enumeration (unknown email same response as known).

## Design

- **LoginToken** — already in SWAN-3; hashed at rest, single-use flag, expiry
- **RequestSignInLink** handler — validates email, issues a token (hashed), sends link via email, rate-limits per email/IP
- **RedeemSignInLink** handler — checks token (hash), verifies single-use and expiry, sets session cookie
- **Session cookie** — HttpOnly, Secure, 30-day duration

## Acceptance criteria

- [ ] Handlers in `Swan.Services` (Identity context)
- [ ] Endpoint files map HTTP to handlers (no business logic in endpoints)
- [ ] Architecture test still passes (endpoints don't reference Data)
- [ ] Testcontainers tests (Services.Tests):
  - [ ] Issue and redeem success flow
  - [ ] Reuse (single-use flag) rejected
  - [ ] Expired token rejected (use fake IClock)
  - [ ] Inactive user rejected
- [ ] Basic rate limit per email and IP (can be simple in-memory or use Redis pattern, dev chooses)
- [ ] Unknown email gets same response as known (no enumeration)
- [ ] `/api/me` returns user + role when signed in, 401 otherwise
- [ ] `dotnet test` passes

## Files to touch

- `src/Swan.Services/Identity/` (handlers)
- `src/Swan.Api/Endpoints/` (RequestSignInLink, RedeemSignInLink)
- `tests/Swan.Services.Tests/` (Testcontainers tests)
- `tests/Swan.Architecture.Tests/` (verify endpoints don't reference Data)
