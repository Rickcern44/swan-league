using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Integrations;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Schedule;

public sealed record LatestLineDashboard(decimal Spread, int? HomeMoneyline, int? AwayMoneyline, LineSource Source, DateTimeOffset EnteredAt, UserId EnteredBy);
public sealed record ResultDraftDashboard(int HomeScore, int AwayScore, ResultDraftSource Source, DateTimeOffset UpdatedAt, string? Reason);
public sealed record GameDashboard(GameId Id, TeamId HomeTeamId, TeamId AwayTeamId, DateTimeOffset Kickoff, bool Excluded, decimal? ApprovedLine, IReadOnlyList<decimal> LineHistory, bool HasResult, int? HomeScore = null, int? AwayScore = null, string? HomeTeamName = null, string? AwayTeamName = null, LatestLineDashboard? LatestLine = null, ResultDraftDashboard? ProposedResult = null);
public sealed record WeekDashboard(WeekId Id, SeasonId SeasonId, int Number, WeekKind Kind, WeekState State, IReadOnlyList<GameDashboard> Games, IReadOnlyList<TeamId> DogOptions, WeekNextAction? NextAction = null, bool CoachPickVisibilityOverride = false, string? CoachPickVisibilityOverrideReason = null);
public sealed record SeasonWeek(WeekId Id, int Number, WeekKind Kind, WeekState State);

public sealed record RecomputedWeek(WeekId WeekId, int Number);
public sealed record FailedWeek(WeekId WeekId, int Number, string Code);
public sealed record SeasonRecomputeSummary(SeasonId SeasonId, IReadOnlyList<RecomputedWeek> Recomputed, IReadOnlyList<FailedWeek> Failed);

public interface IWeekAdministrationService
{
    Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SeasonWeek>>> GetSeasonWeeksAsync(SeasonId seasonId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, AmericanMoneyline? moneyline, LineSource source, bool approve, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ApproveLatestLineAsync(UserId actor, WeekId weekId, GameId gameId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> GenerateDogOptionsAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ReplaceDogOptionsAsync(UserId actor, WeekId weekId, IReadOnlyCollection<TeamId> teamIds, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> SetGameExclusionAsync(UserId actor, WeekId weekId, GameId gameId, bool excluded, string reason, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ApproveLinesAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> PublishAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> LockAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ConfirmResultAsync(UserId actor, WeekId weekId, GameId gameId, int homeScore, int awayScore, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> SubmitResultDraftAsync(UserId actor, WeekId weekId, GameId gameId, int homeScore, int awayScore, string? reason, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ImportFinalScoresAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ConfirmResultDraftsAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> SetCoachPickVisibilityOverrideAsync(UserId actor, WeekId weekId, bool enabled, string? reason, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> FinalizeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> RecomputeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<SeasonRecomputeSummary>> RecomputeSeasonAsync(UserId actor, SeasonId seasonId, CancellationToken cancellationToken = default);
}

public sealed class WeekAdministrationService(SwanDbContext database, IAuditService audit, IClock? clock = null, IFinalScoreProvider? finalScores = null) : IWeekAdministrationService
{
    public async Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default) =>
        await database.Weeks.AsNoTracking().FirstOrDefaultAsync(week => week.Id == weekId, cancellationToken) is { } week
            ? Result<WeekDashboard>.Success(await PresentAsync(week, cancellationToken))
            : Missing;

    public async Task<Result<IReadOnlyList<SeasonWeek>>> GetSeasonWeeksAsync(SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        if (!await database.Seasons.AsNoTracking().AnyAsync(season => season.Id == seasonId, cancellationToken))
            return Result<IReadOnlyList<SeasonWeek>>.Failure("season.missing", "Season does not exist.");
        var weeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == seasonId)
            .OrderBy(week => week.Number).ThenBy(week => week.Id.Value)
            .Select(week => new SeasonWeek(week.Id, week.Number, week.Type.Kind, week.State)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<SeasonWeek>>.Success(weeks);
    }

    public Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, bool approve, CancellationToken cancellationToken = default) =>
        AddLineAsync(actor, weekId, gameId, spread, null, LineSource.Manual, approve, cancellationToken);

    public Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, AmericanMoneyline? moneyline, LineSource source, bool approve, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.line.added", week =>
        {
            var line = Spread.Create(spread);
            if (!line.IsSuccess) return Result<bool>.Failure(line.Error!.Code, line.Error.Message);
            var added = week.AddLine(gameId, line.Value!, moneyline, source, clock?.UtcNow ?? DateTimeOffset.UtcNow, actor);
            return added.IsSuccess && approve ? week.ApproveLatestLine(gameId) : added;
        }, cancellationToken);

    public Task<Result<WeekDashboard>> ApproveLatestLineAsync(UserId actor, WeekId weekId, GameId gameId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.line.approved", week => week.ApproveLatestLine(gameId), cancellationToken);

    public Task<Result<WeekDashboard>> GenerateDogOptionsAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.dog-options.generated", week => week.GenerateDogOptions(), cancellationToken);

    public Task<Result<WeekDashboard>> ReplaceDogOptionsAsync(UserId actor, WeekId weekId, IReadOnlyCollection<TeamId> teamIds, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.dog-options.replaced", week => week.ReplaceDogOptions(teamIds), cancellationToken);

    public Task<Result<WeekDashboard>> SetGameExclusionAsync(UserId actor, WeekId weekId, GameId gameId, bool excluded, string reason, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(reason)
            ? Task.FromResult(Result<WeekDashboard>.Failure("game.exclusion.reason", "An exclusion reason is required."))
            : ActAsync(actor, weekId, "week.game.exclusion.changed", week => week.SetGameExclusion(gameId, excluded), cancellationToken, reason);

    public Task<Result<WeekDashboard>> ApproveLinesAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.lines.approved", week => week.ApproveLines(), cancellationToken);

    public Task<Result<WeekDashboard>> PublishAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.published", week => week.Publish(), cancellationToken);

    public Task<Result<WeekDashboard>> LockAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.locked", week => week.Lock(), cancellationToken);

    public Task<Result<WeekDashboard>> ConfirmResultAsync(UserId actor, WeekId weekId, GameId gameId, int homeScore, int awayScore, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.result.confirmed", week =>
        {
            var result = GameResult.Create(homeScore, awayScore);
            return result.IsSuccess ? week.ConfirmResult(gameId, result.Value!) : Result<bool>.Failure(result.Error!.Code, result.Error.Message);
        }, cancellationToken);

    public Task<Result<WeekDashboard>> SubmitResultDraftAsync(UserId actor, WeekId weekId, GameId gameId, int homeScore, int awayScore, string? reason, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.result.drafted", week =>
        {
            var result = GameResult.Create(homeScore, awayScore);
            if (!result.IsSuccess) return Result<bool>.Failure(result.Error!.Code, result.Error.Message);
            var game = week.Games.SingleOrDefault(value => value.Id == gameId);
            if (game is null) return Result<bool>.Failure("game.missing", "Game does not exist.");
            var source = game.ProposedResult is null ? ResultDraftSource.Manual : ResultDraftSource.Override;
            return week.ProposeResult(gameId, result.Value!, source, clock?.UtcNow ?? DateTimeOffset.UtcNow, reason);
        }, cancellationToken, reason);

    public async Task<Result<WeekDashboard>> ImportFinalScoresAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default)
    {
        var week = await database.Weeks.FirstOrDefaultAsync(value => value.Id == weekId, cancellationToken);
        if (week is null) return Missing;
        if (week.State != WeekState.Locked) return Result<WeekDashboard>.Failure("week.state", "Results can only be reviewed for a locked week.");
        if (finalScores is null) return Result<WeekDashboard>.Failure("scores.config", "A final score provider is not configured.");
        var season = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(value => value.Id == week.SeasonId, cancellationToken);
        if (season is null) return Result<WeekDashboard>.Failure("season.missing", "Season does not exist.");
        var fetched = await finalScores.FetchFinalScoresAsync(season.Year, week.Number, cancellationToken);
        if (!fetched.IsSuccess) return Result<WeekDashboard>.Failure(fetched.Error!.Code, fetched.Error.Message);

        var before = ToDashboard(week);
        var byProviderId = week.Games.Where(game => game.ProviderGameId is not null).ToDictionary(game => game.ProviderGameId!, StringComparer.Ordinal);
        foreach (var score in fetched.Value!.Scores.Where(value => value.Week == week.Number))
        {
            if (!byProviderId.TryGetValue(score.ProviderGameId, out var game)) continue;
            var result = GameResult.Create(score.HomeScore, score.AwayScore);
            if (!result.IsSuccess) continue;
            var proposed = week.ProposeResult(game.Id, result.Value!, ResultDraftSource.Provider, score.UpdatedAt, null);
            if (!proposed.IsSuccess && proposed.Error!.Code != "game.result.confirmed") return Result<WeekDashboard>.Failure(proposed.Error.Code, proposed.Error.Message);
        }

        var after = ToDashboard(week);
        await audit.AppendAsync(actor, "week.results.imported", weekId.Value.ToString(), before, after, null, cancellationToken);
        return Result<WeekDashboard>.Success(await PresentAsync(week, cancellationToken));
    }

    public Task<Result<WeekDashboard>> ConfirmResultDraftsAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.results.confirmed", async week =>
        {
            var confirmed = week.ConfirmProposedResults();
            return confirmed.IsSuccess ? await Finalize(week, cancellationToken) : confirmed;
        }, cancellationToken);

    public Task<Result<WeekDashboard>> SetCoachPickVisibilityOverrideAsync(UserId actor, WeekId weekId, bool enabled, string? reason, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.coach-pick-visibility.overridden", week => week.SetCoachPickVisibilityOverride(enabled, reason), cancellationToken, enabled ? reason : null);

    public Task<Result<WeekDashboard>> FinalizeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.finalized", async week => UnsupportedScoring(week) ?? await Finalize(week, cancellationToken), cancellationToken);

    // Only a Final week can be rescored. Results are frozen once Final (no reopen yet), so today this picks up corrected ballots.
    public Task<Result<WeekDashboard>> RecomputeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.recomputed", async week =>
        {
            if (week.State != WeekState.Final) return Result<bool>.Failure("week.state", "Only a finalized week can be recomputed.");
            return UnsupportedScoring(week) ?? await RebuildScoresAsync(week, cancellationToken);
        }, cancellationToken);

    // Reuses RecomputeAsync per Final regular week, so each one is replaced, audited and saved on its own; a week that fails is reported and the rest continue.
    public async Task<Result<SeasonRecomputeSummary>> RecomputeSeasonAsync(UserId actor, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        if (!await database.Seasons.AnyAsync(season => season.Id == seasonId, cancellationToken)) return Result<SeasonRecomputeSummary>.Failure("season.missing", "Season does not exist.");
        var finals = (await database.Weeks.AsNoTracking().Where(week => week.SeasonId == seasonId && week.State == WeekState.Final).ToListAsync(cancellationToken))
            .Where(week => week.Type.Kind == WeekKind.Regular).OrderBy(week => week.Number).ToList();
        var recomputed = new List<RecomputedWeek>();
        var failed = new List<FailedWeek>();
        foreach (var week in finals)
        {
            var result = await RecomputeAsync(actor, week.Id, cancellationToken);
            database.ChangeTracker.Clear();
            if (result.IsSuccess) recomputed.Add(new RecomputedWeek(week.Id, week.Number));
            else failed.Add(new FailedWeek(week.Id, week.Number, result.Error!.Code));
        }

        return Result<SeasonRecomputeSummary>.Success(new SeasonRecomputeSummary(seasonId, recomputed, failed));
    }

    private static Result<bool>? UnsupportedScoring(Week week) => week.Type.Kind == WeekKind.Regular ? null : Result<bool>.Failure("week.scoring", "Only regular-season weeks can be scored here.");

    private async Task<Result<bool>> Finalize(Week week, CancellationToken cancellationToken)
    {
        var finalized = week.FinalizeResults();
        return finalized.IsSuccess ? await RebuildScoresAsync(week, cancellationToken) : finalized;
    }

    // R17 comes from the season (a week with no stored season keeps the default); the other scoring settings are not configurable yet.
    private async Task<Result<bool>> RebuildScoresAsync(Week week, CancellationToken cancellationToken)
    {
        var settings = new RegularScoringSettings((await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.Id == week.SeasonId, cancellationToken))?.Settings.Rules.TeamMayAlsoBeDog ?? true);
        var ballots = await database.Ballots.Where(ballot => ballot.WeekId == week.Id).ToListAsync(cancellationToken);
        // Nothing enforces one ballot per member and week, so refuse to guess which duplicate counts.
        if (ballots.GroupBy(ballot => ballot.MembershipId).Any(group => group.Count() > 1)) return Result<bool>.Failure("week.ballots", "A member has more than one ballot for this week.");
        var scores = await database.WeekScores.Where(score => score.WeekId == week.Id).ToListAsync(cancellationToken);
        var games = week.Games.Select(game => new ScoredGame(game.Id, game.HomeTeamId, game.AwayTeamId, game.Result, game.ApprovedLine)).ToList();
        foreach (var ballot in ballots)
        {
            var score = scores.FirstOrDefault(value => value.MembershipId == ballot.MembershipId);
            if (score is null)
            {
                score = new WeekScore(new WeekScoreId(Guid.NewGuid()), week.SeasonId, ballot.MembershipId, week.Id);
                database.WeekScores.Add(score);
            }

            score.Recompute(RegularScoringPolicy.Recompute(ballot, games, settings));
        }

        return Result<bool>.Success(true);
    }

    private static Result<WeekDashboard> Missing => Result<WeekDashboard>.Failure("week.missing", "Week does not exist.");

    private Task<Result<WeekDashboard>> ActAsync(UserId actor, WeekId weekId, string action, Func<Week, Result<bool>> mutate, CancellationToken cancellationToken, string? reason = null) =>
        ActAsync(actor, weekId, action, week => Task.FromResult(mutate(week)), cancellationToken, reason);

    private async Task<Result<WeekDashboard>> ActAsync(UserId actor, WeekId weekId, string action, Func<Week, Task<Result<bool>>> mutate, CancellationToken cancellationToken, string? reason = null)
    {
        var week = await database.Weeks.FirstOrDefaultAsync(week => week.Id == weekId, cancellationToken);
        if (week is null) return Missing;
        var before = ToDashboard(week);
        var result = await mutate(week);
        if (!result.IsSuccess) return Result<WeekDashboard>.Failure(result.Error!.Code, result.Error.Message);
        var after = ToDashboard(week);
        // AuditService saves the shared context, so the week change and its audit entry commit together.
        await audit.AppendAsync(actor, action, weekId.Value.ToString(), before, after, reason, cancellationToken);
        return Result<WeekDashboard>.Success(await PresentAsync(week, cancellationToken));
    }

    // What the screen shows: the stored week plus read-only presentation (canonical team names and the next allowed action). The audit trail keeps the plain ToDashboard shape.
    private async Task<WeekDashboard> PresentAsync(Week week, CancellationToken cancellationToken)
    {
        var teamIds = week.Games.SelectMany(game => new[] { game.HomeTeamId, game.AwayTeamId }).Distinct().ToList();
        var names = await database.Teams.AsNoTracking().Where(team => teamIds.Contains(team.Id)).ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        var included = week.Games.Where(game => !game.Excluded).ToList();
        var next = WeekNextAction.For(new WeekSnapshot(week.Id, week.Number, week.State, week.Type.Kind, week.Games.Count, included.Count, included.Count(game => game.ApprovedLine is not null), included.Count(game => game.Result is not null), false, included.Count(game => game.ApprovedLine is not null && game.HasUnapprovedLine)));
        var plain = ToDashboard(week);
        return plain with
        {
            NextAction = next,
            Games = plain.Games.OrderBy(game => game.Kickoff).ThenBy(game => game.Id.Value)
                .Select(game => game with { HomeTeamName = names.GetValueOrDefault(game.HomeTeamId), AwayTeamName = names.GetValueOrDefault(game.AwayTeamId) }).ToList(),
        };
    }

    private static WeekDashboard ToDashboard(Week week) => new(
        week.Id, week.SeasonId, week.Number, week.Type.Kind, week.State,
        week.Games.Select(game => new GameDashboard(game.Id, game.HomeTeamId, game.AwayTeamId, game.Kickoff.Value, game.Excluded, game.ApprovedLine?.Value, game.LineHistory.Select(line => line.Value).ToList(), game.Result is not null, game.Result?.HomeScore, game.Result?.AwayScore, LatestLine: game.LatestLine is { } line ? new LatestLineDashboard(line.Spread.Value, line.HomeMoneyline, line.AwayMoneyline, line.Source, line.EnteredAt, line.EnteredBy) : null, ProposedResult: game.ProposedResult is { } draft ? new ResultDraftDashboard(draft.Result.HomeScore, draft.Result.AwayScore, draft.Source, draft.UpdatedAt, draft.Reason) : null)).ToList(),
        week.DogOptions.Select(option => option.TeamId).ToList(), CoachPickVisibilityOverride: week.HasCoachPickVisibilityOverride, CoachPickVisibilityOverrideReason: week.CoachPickVisibilityOverrideReason);
}
