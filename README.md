# Swan League Tracker

Runnable baseline for the Swan League Tracker: an ASP.NET Core API and React/Vite UI managed by .NET Aspire.

## Prerequisites

- .NET SDK 10
- Node.js 20 or later
- Aspire CLI 13.5 or later
- Podman with its machine running (`podman machine start`)

Swan.Services integration tests require a Docker-compatible container runtime.

## Run locally

Install the UI dependencies once:

```sh
cd src/Swan.Web
npm install
```

From the repository root, start the stack and open the dashboard URL printed by Aspire:

```sh
ASPIRE_CONTAINER_RUNTIME=podman aspire run
```

The dashboard lists the `api` and `web` resources. Open the web URL to see `API response: pong`.

In the Development environment, the API applies the committed `InitialDomain` EF Core migration automatically when it starts. Production deployment intentionally does not perform schema changes at application startup.

## Verify

Run the API endpoint test and format check from the repository root:

```sh
dotnet test Swan.slnx
dotnet format Swan.slnx --verify-no-changes
```

## EF Core design-time commands

The repository pins `dotnet-ef`; restore it once from the repository root:

```sh
dotnet tool restore
```

The design-time factory uses `SWAN_DESIGN_CONNECTION`, or a `-- --connection <connection-string>` argument, and otherwise uses its local Postgres default. Run EF commands from the repository root:

```sh
dotnet ef migrations add <Name> --project src/Swan.Data
dotnet ef migrations list --project src/Swan.Data
dotnet ef migrations script --project src/Swan.Data
dotnet ef migrations has-pending-model-changes --project src/Swan.Data
```

To make the last check suitable for CI, run `./scripts/verify-ef-model.sh`.

Run UI formatting, linting, unit tests, and the production build from `src/Swan.Web`:

```sh
npm run format:check
npm run lint
npm test
npm run build
```

With the Aspire stack running, install Playwright's Chromium browser once and run the E2E test against the `web` URL reported by `aspire describe`:

```sh
npx playwright install chromium
E2E_BASE_URL=http://localhost:<web-port> npm run test:e2e
```

## Troubleshooting

After a sandboxed .NET build, run `dotnet build-server shutdown` before starting the AppHost; AppHost builds require permission to write their user-level runfile cache.
