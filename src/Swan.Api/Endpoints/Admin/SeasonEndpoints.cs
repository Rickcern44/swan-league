using System.Security.Claims;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Services.League;

namespace Swan.Api.Endpoints.Admin;

public sealed record CreateSeasonRequest(int Year, string? Name, bool IsSimulation = false);

public sealed record UpdateSeasonRequest(int Year, string? Name, bool IsSimulation = false);

public sealed record UpdateSeasonMoneyRequest(decimal BuyIn, decimal Main, decimal Dog, decimal RegularSeason, decimal Playoff);

public sealed record UpdateSeasonDeadlineRequest(TimeOnly? TeamPickDeadlineEastern);

public sealed record UpdateSeasonStepRequest(SetupStep? Step);

public sealed record TeamFinishRequest(Guid TeamId, int FinishPosition);

public sealed record UpdateSeasonTeamsRequest(List<TeamFinishRequest?>? Teams);

public sealed record ImportScheduleRequest(string? Fingerprint);

public sealed record UpdateDoubleDogWeeksRequest(List<int>? Weeks);

public static class SeasonEndpoints
{
    public static IEndpointRouteBuilder MapSeasonEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var seasons = endpoints.MapGroup("/api/admin/seasons").RequireAuthorization("Admin");
        seasons.MapPost("", async (CreateSeasonRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                ? Results.Unauthorized()
                : await service.CreateBlankAsync(new UserId(actor), request.Year, request.Name ?? "", request.IsSimulation, cancellationToken) switch
                {
                    { IsSuccess: true } created => Results.Created($"/api/admin/seasons/setup", created.Value),
                    { Error.Code: "season.name" or "season.year" } invalid => Results.BadRequest(invalid.Error),
                    var rejected => Results.Conflict(rejected.Error)
                });
        seasons.MapGet("/setup", async (ISeasonSetupService service, CancellationToken cancellationToken) =>
            await service.GetSetupAsync(cancellationToken) switch
            {
                { IsSuccess: true } found => Results.Ok(found.Value),
                var missing => Results.NotFound(missing.Error)
            });
        seasons.MapPut("/setup", async (UpdateSeasonRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                ? Results.Unauthorized()
                : await service.UpdateSetupBasicsAsync(new UserId(actor), request.Year, request.Name ?? "", request.IsSimulation, cancellationToken) switch
                {
                    { IsSuccess: true } updated => Results.Ok(updated.Value),
                    { Error.Code: "season.name" or "season.year" } invalid => Results.BadRequest(invalid.Error),
                    { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
                    var rejected => Results.Conflict(rejected.Error)
                });
        seasons.MapPut("/setup/money", async (UpdateSeasonMoneyRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                ? Results.Unauthorized()
                : await service.UpdateSetupMoneyAsync(new UserId(actor), new SetupMoney(Usd(request.BuyIn), Usd(request.Main), Usd(request.Dog), Usd(request.RegularSeason), Usd(request.Playoff)), cancellationToken) switch
                {
                    { IsSuccess: true } updated => Results.Ok(updated.Value),
                    { Error.Code: "season.money" } invalid => Results.BadRequest(invalid.Error),
                    { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
                    var rejected => Results.Conflict(rejected.Error)
                });
        seasons.MapPut("/setup/deadline", async (UpdateSeasonDeadlineRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            request.TeamPickDeadlineEastern is not { } deadline
                ? Results.BadRequest(new Error("season.deadline", "A team-pick deadline in Eastern Time is required."))
                : !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                    ? Results.Unauthorized()
                    : await service.UpdateSetupDeadlineAsync(new UserId(actor), deadline, cancellationToken) switch
                    {
                        { IsSuccess: true } updated => Results.Ok(updated.Value),
                        { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
                        var rejected => Results.Conflict(rejected.Error)
                    });
        seasons.MapPut("/setup/step", async (UpdateSeasonStepRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            request.Step is not { } step
                ? Results.BadRequest(new Error("season.step", "A setup step is required."))
                : !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                    ? Results.Unauthorized()
                    : await service.UpdateSetupStepAsync(new UserId(actor), step, cancellationToken) switch
                    {
                        { IsSuccess: true } updated => Results.Ok(updated.Value),
                        { Error.Code: "season.step" } invalid => Results.BadRequest(invalid.Error),
                        { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
                        var rejected => Results.Conflict(rejected.Error)
                    });
        seasons.MapGet("/setup/teams", async (ISeasonSetupService service, CancellationToken cancellationToken) =>
            await service.GetSetupTeamsAsync(cancellationToken) switch
            {
                { IsSuccess: true } found => Results.Ok(found.Value),
                var missing => Results.NotFound(missing.Error)
            });
        seasons.MapPut("/setup/teams", async (UpdateSeasonTeamsRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            request.Teams is null || request.Teams.Any(team => team is null)
                ? Results.BadRequest(new Error("season.teams", "A finish for every team is required."))
                : !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                    ? Results.Unauthorized()
                    : await service.UpdateSetupTeamsAsync(new UserId(actor), request.Teams.Select(team => new TeamFinish(new TeamId(team!.TeamId), team.FinishPosition)).ToList(), cancellationToken) switch
                    {
                        { IsSuccess: true } updated => Results.Ok(updated.Value),
                        { Error.Code: "season.teams" } invalid => Results.BadRequest(invalid.Error),
                        { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
                        var rejected => Results.Conflict(rejected.Error)
                    });
        seasons.MapGet("/setup/schedule", async (ISeasonSetupService service, CancellationToken cancellationToken) =>
            ScheduleResult(await service.GetSetupScheduleAsync(cancellationToken)));
        // Previewing calls the schedule provider but saves nothing; it is a POST so that nothing fetches it by loading a page.
        seasons.MapPost("/setup/schedule/preview", async (ISeasonSetupService service, CancellationToken cancellationToken) =>
            ScheduleResult(await service.PreviewScheduleAsync(cancellationToken)));
        seasons.MapPost("/setup/schedule/import", async (ImportScheduleRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            string.IsNullOrWhiteSpace(request.Fingerprint)
                ? Results.BadRequest(new Error("schedule.fingerprint", "Preview the schedule and confirm that preview."))
                : !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                    ? Results.Unauthorized()
                    : ScheduleResult(await service.ImportScheduleAsync(new UserId(actor), request.Fingerprint, cancellationToken)));
        seasons.MapGet("/setup/double-dog-weeks", async (ISeasonSetupService service, CancellationToken cancellationToken) =>
            DoubleDogResult(await service.GetSetupDoubleDogWeeksAsync(cancellationToken)));
        seasons.MapPut("/setup/double-dog-weeks", async (UpdateDoubleDogWeeksRequest request, ClaimsPrincipal principal, ISeasonSetupService service, CancellationToken cancellationToken) =>
            request.Weeks is null
                ? Results.BadRequest(new Error("season.double-dogs", "Send the double-dog weeks you chose."))
                : !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                    ? Results.Unauthorized()
                    : DoubleDogResult(await service.UpdateSetupDoubleDogWeeksAsync(new UserId(actor), request.Weeks, cancellationToken)));
        return endpoints;
    }

    // Provider problems are the server's to report: 503 when it is not configured, 502 when the provider fails, 422 when its data cannot be imported.
    private static IResult ScheduleResult(Result<SchedulePlan> result) => result switch
    {
        { IsSuccess: true } ok => Results.Ok(ok.Value),
        { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
        { Error.Code: "schedule.config" } unconfigured => Results.Json(unconfigured.Error, statusCode: StatusCodes.Status503ServiceUnavailable),
        { Error.Code: "schedule.provider" } unavailable => Results.Json(unavailable.Error, statusCode: StatusCodes.Status502BadGateway),
        { Error.Code: "schedule.invalid" or "schedule.empty" } unusable => Results.Json(unusable.Error, statusCode: StatusCodes.Status422UnprocessableEntity),
        var rejected => Results.Conflict(rejected.Error)
    };

    private static IResult DoubleDogResult(Result<DoubleDogPlan> result) => result switch
    {
        { IsSuccess: true } ok => Results.Ok(ok.Value),
        { Error.Code: "season.double-dogs" } invalid => Results.BadRequest(invalid.Error),
        { Error.Code: "season.missing" } missing => Results.NotFound(missing.Error),
        var rejected => Results.Conflict(rejected.Error)
    };

    private static Money Usd(decimal amount) => new(amount, "USD");
}
