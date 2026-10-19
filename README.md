# Swan League Tracker

Swan League Tracker is an ASP.NET Core API and React/Vite application managed by .NET Aspire.

## Documentation

- [Product requirements](docs/reference/Swan%20League%20App%20%E2%80%94%20Product%20Requirements.md) define product scope and delivery phases.
- [Project status](docs/project.md) summarizes implemented work, evidence, and the next milestone.
- [Feature history](docs/features/README.md) records the implementation work that led here.

## Prerequisites

- .NET SDK 10
- Node.js 20 or later
- Aspire CLI 13.5 or later
- Podman with its machine running (`podman machine start`)

Swan.Services integration tests require a Docker-compatible container runtime.

## Run locally

The default Aspire stack uses a disposable Postgres container. This prevents stale local
volumes from retaining an old generated password and blocking startup. If you intentionally
need the database to persist across restarts, start Aspire with `UseVolumes=true`.

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

Development startup also seeds the 32 NFL teams, final 2025 division finishes, a demo season, and demo users (`admin@swan.local`, `coach1@swan.local` through `coach6@swan.local`). The seed is idempotent. The checked-in standings data is [nfl-2025-finishes.json](src/Swan.Data/Seed/nfl-2025-finishes.json), sourced from the [official NFL division standings](https://www.nfl.com/standings/division/2025/POST).

To reset local development data, drop the database and start the development stack again so migrations and seed data are reapplied:

```sh
dotnet ef database drop --project src/Swan.Data --force
ASPIRE_CONTAINER_RUNTIME=podman aspire run
```

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
npm run format:check # Biome
npm run lint         # Biome
npm run check        # Biome formatter, linter, and import organization
npm test
npm run build
```

Install Playwright's Chromium browser once. The E2E runner starts an isolated Aspire stack with a disposable Postgres database, waits for the app, discovers the web and Mailpit URLs, runs the browser flow, and stops the stack:

```sh
cd src/Swan.Web
npx playwright install chromium
ASPIRE_CONTAINER_RUNTIME=podman npm run test:e2e:stack
```

The runner refuses to start while another AppHost is running, so it cannot accidentally test against your development database. In CI it uses Docker; locally set `ASPIRE_CONTAINER_RUNTIME=podman` as above. Each run gets fresh seeded data, so no SQL reset commands are needed.

## Schedule provider key

The Schedule import page reads the NFL schedule from BALLDONTLIE. Put the API key in the API project's .NET user secrets (never in a file you commit):

```sh
dotnet user-secrets set "ExternalServices:BallDontLie:ApiKey" "<your key>" --project src/Swan.Api
```

Without it the preview reports that the provider is not configured. The provider rate limits requests, and a preview of a full season takes three, so wait a minute between previews.

## Troubleshooting

After a sandboxed .NET build, run `dotnet build-server shutdown` before starting the AppHost; AppHost builds require permission to write their user-level runfile cache.
