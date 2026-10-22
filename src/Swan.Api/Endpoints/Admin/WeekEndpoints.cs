using System.Security.Claims;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Services.Schedule;

namespace Swan.Api.Endpoints.Admin;

public sealed record MoneylineRequest(int Home, int Away);
public sealed record AddLineRequest(decimal Spread, bool Approve = false, MoneylineRequest? Moneyline = null, string? Source = null);
public sealed record SetGameExclusionRequest(bool Excluded, string? Reason);
public sealed record SeasonWeekResponse(Guid Id, int Number, string Kind, string State);
public sealed record ConfirmResultRequest(int HomeScore, int AwayScore);
public sealed record ResultDraftRequest(int HomeScore, int AwayScore, string? Reason);
public sealed record ReplaceDogOptionsRequest(IReadOnlyList<Guid> TeamIds);

public static class WeekEndpoints
{
    public static IEndpointRouteBuilder MapWeekEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var weeks = endpoints.MapGroup("/api/admin/weeks/{weekId:guid}").RequireAuthorization("Admin");
        weeks.MapGet("", async (Guid weekId, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            ToResult(await service.GetAsync(new WeekId(weekId), cancellationToken)));
        endpoints.MapGet("/api/admin/seasons/{seasonId:guid}/weeks", async (Guid seasonId, IWeekAdministrationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetSeasonWeeksAsync(new SeasonId(seasonId), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value!.Select(week => new SeasonWeekResponse(week.Id.Value, week.Number, week.Kind.ToString(), week.State.ToString())))
                : Results.NotFound(result.Error);
        }).RequireAuthorization("Admin");
        weeks.MapPost("/games/{gameId:guid}/lines", (Guid weekId, Guid gameId, AddLineRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
        {
            var source = ParseSource(request.Source);
            if (!source.IsSuccess) return Task.FromResult<IResult>(Results.BadRequest(source.Error));
            var parsedMoneyline = request.Moneyline is null ? null : AmericanMoneyline.Create(request.Moneyline.Home, request.Moneyline.Away);
            var moneyline = parsedMoneyline is null
                ? Result<AmericanMoneyline?>.Success(null)
                : parsedMoneyline.IsSuccess
                    ? Result<AmericanMoneyline?>.Success(parsedMoneyline.Value)
                    : Result<AmericanMoneyline?>.Failure(parsedMoneyline.Error!.Code, parsedMoneyline.Error.Message);
            return !moneyline.IsSuccess
                ? Task.FromResult<IResult>(Results.BadRequest(moneyline.Error))
                : Act(principal, actor => service.AddLineAsync(actor, new WeekId(weekId), new GameId(gameId), request.Spread, moneyline.Value, source.Value!, request.Approve, cancellationToken));
        });
        weeks.MapPost("/games/{gameId:guid}/lines/approve-latest", (Guid weekId, Guid gameId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ApproveLatestLineAsync(actor, new WeekId(weekId), new GameId(gameId), cancellationToken)));
        weeks.MapPost("/games/{gameId:guid}/exclusion", (Guid weekId, Guid gameId, SetGameExclusionRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.SetGameExclusionAsync(actor, new WeekId(weekId), new GameId(gameId), request.Excluded, request.Reason ?? string.Empty, cancellationToken)));
        weeks.MapPost("/dog-options/generate", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.GenerateDogOptionsAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPut("/dog-options", (Guid weekId, ReplaceDogOptionsRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            request.TeamIds is null
                ? Task.FromResult(Results.BadRequest(new Error("week.dogs", "teamIds is required.")))
                : Act(principal, actor => service.ReplaceDogOptionsAsync(actor, new WeekId(weekId), request.TeamIds.Select(id => new TeamId(id)).ToList(), cancellationToken)));
        weeks.MapPost("/lines/approve", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ApproveLinesAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/publish", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.PublishAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/games/{gameId:guid}/result", (Guid weekId, Guid gameId, ConfirmResultRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ConfirmResultAsync(actor, new WeekId(weekId), new GameId(gameId), request.HomeScore, request.AwayScore, cancellationToken)));
        weeks.MapPost("/games/{gameId:guid}/result-draft", (Guid weekId, Guid gameId, ResultDraftRequest request, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.SubmitResultDraftAsync(actor, new WeekId(weekId), new GameId(gameId), request.HomeScore, request.AwayScore, request.Reason, cancellationToken)));
        weeks.MapPost("/results/import", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ImportFinalScoresAsync(actor, new WeekId(weekId), cancellationToken)));
        weeks.MapPost("/results/confirm", (Guid weekId, ClaimsPrincipal principal, IWeekAdministrationService service, CancellationToken cancellationToken) =>
            Act(principal, actor => service.ConfirmResultDraftsAsync(actor, new WeekId(weekId), cancellationToken)));
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
        { IsSuccess: true } => Results.Ok(result.Value is WeekDashboard week ? WeekResponse.From(week) : result.Value),
        { Error.Code: "week.missing" or "game.missing" or "season.missing" } => Results.NotFound(result.Error),
        { Error.Code: "spread.invalid" or "game-result.invalid" or "moneyline.invalid" or "game.exclusion.reason" or "result.override.reason" } => Results.BadRequest(result.Error),
        _ => Results.Conflict(result.Error)
    };

    private static Result<LineSource> ParseSource(string? source) => string.IsNullOrWhiteSpace(source) || string.Equals(source, "manual", StringComparison.OrdinalIgnoreCase)
        ? Result<LineSource>.Success(LineSource.Manual)
        : string.Equals(source, "csv", StringComparison.OrdinalIgnoreCase)
            ? Result<LineSource>.Success(LineSource.Csv)
            : Result<LineSource>.Failure("line.source", "Source must be manual or csv.");
}
