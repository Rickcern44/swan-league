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

The E2E test edits the Setup season and puts it back, so run it only against a dedicated test database, never your development data (all `aspire run --isolated` stacks share one Postgres volume unless the AppHost names its own). There is no delete endpoint: if the test has to create the Setup season it removes it with `E2E_RESET_SETUP_SEASON_CMD`, and if it fills in teams on a season that had none it clears them with `E2E_RESET_SETUP_TEAMS_CMD`; without those commands the run fails rather than leave data behind. For example (the generated Postgres password contains parentheses, so keep `$POSTGRES_PASSWORD` inside the single-quoted `sh -c`):

```sh
export E2E_RESET_SETUP_TEAMS_CMD="podman exec <postgres container> sh -c 'PGPASSWORD=\"\$POSTGRES_PASSWORD\" psql -U postgres -d swan -c \"delete from schedule.season_teams where \\\"SeasonId\\\" in (select \\\"Id\\\" from league.seasons where \\\"State\\\" = 0)\"'"
```

If the test saves fines and payouts on a season that had none configured, `E2E_RESET_SETUP_PAYOUTS_CMD` puts that back (for example `update league.seasons set "PayoutPlans" = (chr(91) || chr(93)), "MissedPickFine" = 5.00 where "State" = 0`; `chr(91) || chr(93)` is `[]` and avoids nested shell quotes). `E2E_RESET_SETUP_SEASON_CMD` is the same with `...; delete from league.seasons where "State" = 0` appended. Sign-in links are limited to one per address per minute, so wait a minute between runs.

## Schedule provider key

The Schedule import page reads the NFL schedule from BALLDONTLIE. Put the API key in the API project's .NET user secrets (never in a file you commit):

```sh
dotnet user-secrets set "ExternalServices:BallDontLie:ApiKey" "<your key>" --project src/Swan.Api
```

Without it the preview reports that the provider is not configured. The provider rate limits requests, and a preview of a full season takes three, so wait a minute between previews.

## Troubleshooting

After a sandboxed .NET build, run `dotnet build-server shutdown` before starting the AppHost; AppHost builds require permission to write their user-level runfile cache.
