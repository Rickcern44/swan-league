using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Schedule;

public sealed record GameDashboard(GameId Id, TeamId HomeTeamId, TeamId AwayTeamId, DateTimeOffset Kickoff, bool Excluded, decimal? ApprovedLine, IReadOnlyList<decimal> LineHistory, bool HasResult, int? HomeScore = null, int? AwayScore = null);
public sealed record WeekDashboard(WeekId Id, SeasonId SeasonId, int Number, WeekKind Kind, WeekState State, IReadOnlyList<GameDashboard> Games);

public interface IWeekAdministrationService
{
    Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, bool approve, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ApproveLatestLineAsync(UserId actor, WeekId weekId, GameId gameId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ApproveLinesAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> PublishAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> LockAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ConfirmResultAsync(UserId actor, WeekId weekId, GameId gameId, int homeScore, int awayScore, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> FinalizeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> RecomputeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
}

public sealed class WeekAdministrationService(SwanDbContext database, IAuditService audit) : IWeekAdministrationService
{
    public async Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default) =>
        await database.Weeks.AsNoTracking().FirstOrDefaultAsync(week => week.Id == weekId, cancellationToken) is { } week
            ? Result<WeekDashboard>.Success(ToDashboard(week))
            : Missing;

    public Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, bool approve, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.line.added", week =>
        {
            var line = Spread.Create(spread);
            if (!line.IsSuccess) return Result<bool>.Failure(line.Error!.Code, line.Error.Message);
            var added = week.AddLine(gameId, line.Value!);
            return added.IsSuccess && approve ? week.ApproveLatestLine(gameId) : added;
        }, cancellationToken);

    public Task<Result<WeekDashboard>> ApproveLatestLineAsync(UserId actor, WeekId weekId, GameId gameId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.line.approved", week => week.ApproveLatestLine(gameId), cancellationToken);

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

    public Task<Result<WeekDashboard>> FinalizeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.finalized", async week => UnsupportedScoring(week) ?? await Finalize(week, cancellationToken), cancellationToken);

    // Only a Final week can be rescored; a corrected source (result or ballot) is picked up by calling this again.
    public Task<Result<WeekDashboard>> RecomputeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.recomputed", async week =>
        {
            if (week.State != WeekState.Final) return Result<bool>.Failure("week.state", "Only a finalized week can be recomputed.");
            return UnsupportedScoring(week) ?? await RebuildScoresAsync(week, cancellationToken);
        }, cancellationToken);

    private static Result<bool>? UnsupportedScoring(Week week) => week.Type.Kind == WeekKind.Regular ? null : Result<bool>.Failure("week.scoring", "Only regular-season weeks can be scored here.");

    private async Task<Result<bool>> Finalize(Week week, CancellationToken cancellationToken)
    {
        var finalized = week.FinalizeResults();
        return finalized.IsSuccess ? await RebuildScoresAsync(week, cancellationToken) : finalized;
    }

    // Scoring settings are not persisted yet, so the policy defaults apply.
    private async Task<Result<bool>> RebuildScoresAsync(Week week, CancellationToken cancellationToken)
    {
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

            score.Recompute(RegularScoringPolicy.Recompute(ballot, games, new RegularScoringSettings()));
        }

        return Result<bool>.Success(true);
    }

    private static Result<WeekDashboard> Missing => Result<WeekDashboard>.Failure("week.missing", "Week does not exist.");

    private Task<Result<WeekDashboard>> ActAsync(UserId actor, WeekId weekId, string action, Func<Week, Result<bool>> mutate, CancellationToken cancellationToken) =>
        ActAsync(actor, weekId, action, week => Task.FromResult(mutate(week)), cancellationToken);

    private async Task<Result<WeekDashboard>> ActAsync(UserId actor, WeekId weekId, string action, Func<Week, Task<Result<bool>>> mutate, CancellationToken cancellationToken)
    {
        var week = await database.Weeks.FirstOrDefaultAsync(week => week.Id == weekId, cancellationToken);
        if (week is null) return Missing;
        var before = ToDashboard(week);
        var result = await mutate(week);
        if (!result.IsSuccess) return Result<WeekDashboard>.Failure(result.Error!.Code, result.Error.Message);
        var after = ToDashboard(week);
        // AuditService saves the shared context, so the week change and its audit entry commit together.
        await audit.AppendAsync(actor, action, weekId.Value.ToString(), before, after, cancellationToken: cancellationToken);
        return Result<WeekDashboard>.Success(after);
    }

    private static WeekDashboard ToDashboard(Week week) => new(
        week.Id, week.SeasonId, week.Number, week.Type.Kind, week.State,
        week.Games.Select(game => new GameDashboard(game.Id, game.HomeTeamId, game.AwayTeamId, game.Kickoff.Value, game.Excluded, game.ApprovedLine?.Value, game.LineHistory.Select(line => line.Value).ToList(), game.Result is not null, game.Result?.HomeScore, game.Result?.AwayScore)).ToList());
}
