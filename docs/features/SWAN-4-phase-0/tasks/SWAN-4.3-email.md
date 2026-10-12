---
id: SWAN-4.3
title: Email sender via Mailpit
status: done
parent: SWAN-4
---

# SWAN-4.3: Email sender via Mailpit

Intent: Implement IEmailSender interface (Core, per PRD §10.1) with SMTP implementation in Services/Integrations wired to the Aspire Mailpit resource. Dev mode sends to Mailpit; production would use a real provider (out of scope for Phase 0).

## Design

- **IEmailSender** — interface in `Swan.Core` (recipient, subject, body, HTML flag)
- **SmtpEmailSender** — implementation in `Swan.Services/Integrations`, wired to Mailpit via Aspire resource
- **Mailpit integration** — Aspire defines the container; apphost.cs wires it to the API

## Acceptance criteria

- [x] IEmailSender interface in Swan.Core
- [x] SmtpEmailSender implementation in Swan.Services/Integrations
- [x] Integration test (Testcontainers Mailpit or Aspire resource) sends a message and reads it back via Mailpit API
- [x] `aspire run` brings up Mailpit; emails are visible in the dashboard
- [x] No real email provider code
- [x] `dotnet test` passes

## Files to touch

- new `src/Swan.Core/Integrations/IEmailSender.cs` (or similar)
- new `src/Swan.Services/Integrations/SmtpEmailSender.cs`
- `apphost.cs` (wire Mailpit)
- `tests/Swan.Services.Tests/` (add Mailpit integration test)
