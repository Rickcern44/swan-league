using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Schedule;

namespace Swan.Api.Endpoints.Admin;

public sealed record AddLineRequest(decimal Spread, bool Approve = false);
public sealed record ConfirmResultRequest(int HomeScore, int AwayScore);

public static class WeekEndpoints
{
    public static IEndpointRouteBuilder MapWeekEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var weeks = endpoints.MapGroup("/api/admin/weeks/{weekId:guid}").RequireAuthorization("Admin");
        weeks.MapGet("", async (Guid weekId, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            ToResult(await service.GetAsync(new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/games/{gameId:guid}/lines", (Guid weekId, Guid gameId, AddLineRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.AddLineAsync(actor, new WeekId(weekId), new GameId(gameId), request.Spread, request.Approve, cancellationToken)));
        weeks.MapPost("/games/{gameId:guid}/lines/approve-latest", (Guid weekId, Guid gameId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ApproveLatestLineAsync(actor, new WeekId(weekId), new GameId(gameId), cancellationToken)));
        weeks.MapPost("/lines/approve", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ApproveLinesAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/publish", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.PublishAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/games/{gameId:guid}/result", (Guid weekId, Guid gameId, ConfirmResultRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ConfirmResultAsync(actor, new WeekId(weekId), new GameId(gameId), request.HomeScore, request.AwayScore, cancellationToken)));
        weeks.MapPost("/finalize", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.FinalizeAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/recompute", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.RecomputeAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/lock", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.LockAsync(actor, new WeekId(weekId), cancellationToken)));
        endpoints.MapPost("/api/admin/seasons/{seasonId:guid}/weeks/recompute", (Guid seasonId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.RecomputeSeasonAsync(actor, new SeasonId(seasonId), cancellationToken))).RequireAuthorization("Admin");
        return endpoints;
    }

    private static async Task<IResult> Act<T>(ClaimsPrincipal principal, Func<UserId, Task<Result<T>>> action) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? ToResult(await action(new UserId(actor))) : Results.Unauthorized();

    private static IResult ToResult<T>(Result<T> result) => result switch
    {
        { IsSuccess: true } => Results.Ok(result.Value),
        { Error.Code: "week.missing" or "game.missing" or "season.missing" } => Results.NotFound(result.Error),
        _ => Results.Conflict(result.Error)
    };
}
