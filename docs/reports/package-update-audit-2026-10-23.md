# Package update audit — 2026-10-23

## Scope and method

This is a package-currency audit, not a vulnerability remediation plan. It
compares the repository's direct .NET, local-tool, and npm dependencies with
the public registries on 2026-10-23. No package versions or lockfiles were
changed by this audit.

Sources and commands:

- `dotnet list Swan.slnx package --outdated` queried NuGet for stable direct
  package updates.
- `dotnet list Swan.slnx package --outdated --include-prerelease` was used to
  identify available prerelease major versions without recommending them.
- `dotnet tool search dotnet-ef --take 1` and `dotnet tool search aspire.cli
  --take 1` queried local-tool package versions.
- `npm outdated --include=dev --json --prefix src/Swan.Web` queried the npm
  lockfile's direct production dependencies. Because this npm installation did
  not report development dependencies, `npm view <package> version` was run for
  every direct development dependency.

## Recommended update batches

| Priority | Batch | Current → target | Rationale and required validation |
| --- | --- | --- | --- |
| 1 | EF Core design tools | `Microsoft.EntityFrameworkCore.Design` and `dotnet-ef`: 10.0.3 → 10.0.13 | Update the API, Data, and local-tool references together to preserve their documented version alignment. Keep Npgsql at 10.0.3; it is current in the 10.x line. Run EF model verification and the full .NET test suite. |
| 1 | Aspire family | AppHost SDK, three hosting packages, Npgsql integration, AppHost testing, and `aspire.cli`: 13.6.1 → 13.6.3 | Keep the Aspire family aligned. Evaluate `CommunityToolkit.Aspire.Hosting.MailPit` 13.6.1-beta.930 → beta.935 as part of the same AppHost smoke test, but do not treat it as a stable upgrade. Start the AppHost and run the stack E2E suite. |
| 1 | Microsoft.Extensions | `Http.Resilience` and `ServiceDiscovery`: 10.8.0 → 10.11.0; `Http`: 10.0.5 → 10.0.13; `Hosting`: 10.0.9 → 10.0.13 | These are same-major updates. Apply after the Aspire batch or in a focused follow-up, then run the .NET tests. |
| 2 | OpenTelemetry family | Five packages: 1.15.1–1.15.3 → 1.20.1 | Upgrade all five together. This spans multiple minor releases and needs telemetry/startup verification, not isolated package edits. |
| 2 | React and Playwright | `react` and `react-dom`: 19.1.1 → 19.3.1; `@playwright/test`: 1.64.0 → 1.64.1 | React and React DOM must stay aligned. Regenerate `package-lock.json`, run unit/build checks, and run E2E tests after the Playwright update. |
| 3 | Frontend build/test majors | Vite 6.3.5 → 8.3.5; `@vitejs/plugin-react` 4.7.0 → 6.1.2; Vitest 3.2.4 → 5.0.5 | Make this a dedicated migration. Check Node 24 compatibility, Vite/PWA configuration, TypeScript compilation, test APIs, and E2E behavior. |
| 3 | Remaining frontend development packages | Biome 2.5.15 → 2.6.2; Testing Library DOM 6.6.3 → 7.0.1; Testing Library React 16.3.0 → 16.3.3; Node types 22.15.34 → 26.6.5; React types 19.1.8/19.1.6 → 19.3.0; jsdom 26.1.0 → 30.1.3; TypeScript 5.8.3 → 7.0.2 | Several are major updates, particularly jsdom and TypeScript. Evaluate as a separate frontend tooling migration rather than mixing them into the React patch update. |

## Current packages with no stable direct update found

- .NET: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3,
  `Microsoft.AspNetCore.Mvc.Testing` 10.0.13, Testcontainers 4.16.0 and
  `Testcontainers.PostgreSql` 4.16.0, `xunit.v3.mtp-v2` 4.0.2, and
  `NetArchTest.Rules` 1.3.2.
- npm production dependencies: `@base-ui/react` 1.9.0,
  `@fontsource-variable/geist` 5.3.0, `class-variance-authority` 0.7.1,
  `cn` 0.5.0, `lucide-react` resolves to 1.61.0 under its declared range, and
  `tw-animate-css` 1.4.0.
- npm development dependencies: `@tailwindcss/vite` 4.3.3, `shadcn` 4.22.1,
  `tailwindcss` 4.3.3, and `vite-plugin-pwa` 2.0.0.

The stable NuGet command intentionally excludes the available EF Core, Npgsql,
and Microsoft.Extensions 11.0 release candidates. They are outside this
application's `net10.0` major-version update scope.

## Implementation notes

- CI restores .NET in locked mode and uses `npm ci`; every approved update
  needs the corresponding lockfile regenerated with supported restore/install
  commands.
- Do not combine the Vite/plugin-react/Vitest or TypeScript/jsdom major
  migrations with low-risk patches.
- The NuGet audit emitted `NU1902` for transitive `MessagePack` 2.5.302 in the
  AppHost projects. That is a vulnerability warning, outside this currency
  audit, and should be triaged separately with a dependency-path and advisory
  review.

For each implementation batch, run `dotnet restore Swan.slnx --locked-mode`,
the Release build, `dotnet test Swan.slnx`, `./scripts/verify-ef-model.sh`, and
`dotnet format Swan.slnx --verify-no-changes`. For frontend batches also run
`npm ci`, `npm run check`, `npm test`, and `npm run build` in `src/Swan.Web`.
Runtime-facing upgrades require the Aspire-backed `npm run test:e2e:stack`
check against its dedicated disposable database.
