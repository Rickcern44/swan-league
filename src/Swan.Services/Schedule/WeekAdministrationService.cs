using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Schedule;

public sealed record GameDashboard(GameId Id, TeamId HomeTeamId, TeamId AwayTeamId, DateTimeOffset Kickoff, bool Excluded, decimal? ApprovedLine, bool HasResult);
public sealed record WeekDashboard(WeekId Id, SeasonId SeasonId, int Number, WeekKind Kind, WeekState State, IReadOnlyList<GameDashboard> Games);

public interface IWeekAdministrationService
{
    Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, bool approve, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> ApproveLinesAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> PublishAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<WeekDashboard>> LockAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default);
}

public sealed class WeekAdministrationService(SwanDbContext database, IAuditService audit) : IWeekAdministrationService
{
    public async Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default) =>
        await database.Weeks.AsNoTracking().FirstOrDefaultAsync(week => week.Id == weekId, cancellationToken) is { } week
            ? Result<WeekDashboard>.Success(ToDashboard(week))
            : Missing;

    // Line history is not persisted, so a line can only be approved in the request that adds it.
    public Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, bool approve, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.line.added", week =>
        {
            var line = Spread.Create(spread);
            if (!line.IsSuccess) return Result<bool>.Failure(line.Error!.Code, line.Error.Message);
            var added = week.AddLine(gameId, line.Value!);
            return added.IsSuccess && approve ? week.ApproveLatestLine(gameId) : added;
        }, cancellationToken);

    public Task<Result<WeekDashboard>> ApproveLinesAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.lines.approved", week => week.ApproveLines(), cancellationToken);

    public Task<Result<WeekDashboard>> PublishAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.published", week => week.Publish(), cancellationToken);

    public Task<Result<WeekDashboard>> LockAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) =>
        ActAsync(actor, weekId, "week.locked", week => week.Lock(), cancellationToken);

    private static Result<WeekDashboard> Missing => Result<WeekDashboard>.Failure("week.missing", "Week does not exist.");

    private async Task<Result<WeekDashboard>> ActAsync(UserId actor, WeekId weekId, string action, Func<Week, Result<bool>> mutate, CancellationToken cancellationToken)
    {
        var week = await database.Weeks.FirstOrDefaultAsync(week => week.Id == weekId, cancellationToken);
        if (week is null) return Missing;
        var before = ToDashboard(week);
        var result = mutate(week);
        if (!result.IsSuccess) return Result<WeekDashboard>.Failure(result.Error!.Code, result.Error.Message);
        var after = ToDashboard(week);
        // AuditService saves the shared context, so the week change and its audit entry commit together.
        await audit.AppendAsync(actor, action, weekId.Value.ToString(), before, after, cancellationToken: cancellationToken);
        return Result<WeekDashboard>.Success(after);
    }

    private static WeekDashboard ToDashboard(Week week) => new(
        week.Id, week.SeasonId, week.Number, week.Type.Kind, week.State,
        week.Games.Select(game => new GameDashboard(game.Id, game.HomeTeamId, game.AwayTeamId, game.Kickoff.Value, game.Excluded, game.ApprovedLine?.Value, game.Result is not null)).ToList());
}
