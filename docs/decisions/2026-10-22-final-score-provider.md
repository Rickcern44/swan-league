# Final-score provider decision

## Decision

Use **BALLDONTLIE NFL** as Swan's primary final-score provider for the MVP. Keep manual result entry and commissioner confirmation as the authority for scoring. Add The Odds API only as a short-window operational fallback if BALLDONTLIE is unavailable or rate-limited.

## Evidence

- The [BALLDONTLIE NFL games API](https://nfl.balldontlie.io/) documents `status_state`, including `final`, with `home_team_score`, `visitor_team_score`, team names, a game ID, week, and `updated_at`. The free NFL tier includes Games and allows 5 requests per minute; Games are listed from 2002 onward.
- A live verification on 2026-10-22 queried the configured local BALLDONTLIE account for 2025 regular-season week 1. It returned completed games with the documented shape. For example, provider game `423945` mapped to Philadelphia Eagles 24, Dallas Cowboys 20, state `final`, with its provider update timestamp. No key or request credential was recorded.
- The new `IFinalScoreProvider` contract and `BallDontLieScheduleProvider.FetchFinalScoresAsync` map only `status_state: final` games to a provider game ID, week, teams, scores, and update timestamp. Unit coverage verifies final-only filtering and the mapping.
- BALLDONTLIE permits use of its data in an application's own backend API, but prohibits presenting it as a substitute sports-data feed. Its [terms](https://www.balldontlie.io/terms.html) also describe data as best-effort and advise independent verification for time-sensitive decisions. Swan's commissioner confirmation and manual override satisfy that constraint.

## Operating assumptions

- Poll the currently locked week through one regular-season games request at a time, starting roughly three hours after kickoff, then stop once every included game is final or the commissioner acts.
- Keep calls below the free-tier 5 requests/minute limit. Use exponential backoff after HTTP 429 or a provider failure; do not parallelize league-week polling.
- Treat missing, delayed, corrected, or malformed provider data as a draft import only. It never changes a confirmed Swan result automatically.
- Store API keys only in user secrets or deployment secret stores. The adapter sends the key exclusively in the `Authorization` header and never logs it.

## Fallback

The [The Odds API scores endpoint](https://the-odds-api.com/liveapi/guides/v4/index.html) supports NFL scores, including recently completed games, and exposes quota headers. It is feasible as an operational fallback but retains completed games for only up to three days, so it cannot support historical backfill or delayed recovery. Its [terms](https://the-odds-api.com/terms-and-conditions.html) permit displaying/storing data in an application but prohibit resale as a raw data service.

If both providers fail, the commissioner enters the result manually and records the override reason. No provider key is committed to this repository.

## Follow-up

Issue #13 should use `IFinalScoreProvider` to import a matching result by the schedule's persisted `ProviderGameId`, present it as a reviewable draft, and require commissioner confirmation before recalculating standings.
