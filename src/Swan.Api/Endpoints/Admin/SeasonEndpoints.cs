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
        return endpoints;
    }

    private static Money Usd(decimal amount) => new(amount, "USD");
}
