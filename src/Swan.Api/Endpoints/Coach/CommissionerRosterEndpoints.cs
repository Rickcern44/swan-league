using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Picks;

namespace Swan.Api.Endpoints.Coach;

public sealed record CorrectRosterRequest(Guid? SeasonId, Guid? MembershipId, int? ExpectedRevision, Guid? RemoveTeamId, Guid? AddTeamId, string? Reason);

public static class CommissionerRosterEndpoints
{
    // Authority is the caller's own Commissioner membership of the live season, checked by the service: the Admin role alone, a coach, or another season's commissioner is refused with 403.
    public static IEndpointRouteBuilder MapCommissionerRosterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var draft = endpoints.MapGroup("/api/commissioner/roster-draft").RequireAuthorization();
        draft.MapGet("", async (ClaimsPrincipal principal, ICommissionerRosterService service, CancellationToken cancellationToken) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                ? ToResult(await service.GetAsync(new UserId(actor), cancellationToken))
                : Results.Unauthorized());
        draft.MapPost("/corrections", async (CorrectRosterRequest request, ClaimsPrincipal principal, ICommissionerRosterService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            if (request.SeasonId is not { } seasonId || request.MembershipId is not { } membershipId || request.ExpectedRevision is not { } revision || request.AddTeamId is not { } add)
                return Results.BadRequest(new Error("roster.request", "seasonId, membershipId, expectedRevision and addTeamId are required."));
            if (revision < 0) return Results.BadRequest(new Error("roster.request", "expectedRevision cannot be negative."));
            return ToResult(await service.CorrectAsync(new UserId(actor), new CorrectRoster(seasonId, membershipId, revision, request.RemoveTeamId, add, request.Reason ?? ""), cancellationToken));
        });
        return endpoints;
    }

    private static IResult ToResult(Result<CommissionerDraft> result) => result switch
    {
        { IsSuccess: true } => Results.Ok(result.Value),
        { Error.Code: "commissioner.required" } => Results.Json(result.Error, statusCode: StatusCodes.Status403Forbidden),
        { Error.Code: "season.missing" or "coach.missing" } => Results.NotFound(result.Error),
        { Error.Code: "roster.reason" or "roster.team" or "roster.correction.invalid" or "roster.duplicate" or "roster.count" } => Results.BadRequest(result.Error),
        _ => Results.Conflict(result.Error)
    };
}
