using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Picks;

namespace Swan.Api.Endpoints.Coach;

// Every field is optional in the contract so a missing one is a readable 400 instead of a model-binding failure.
public sealed record SaveRosterRequest(Guid? SeasonId, int? ExpectedRevision, IReadOnlyList<Guid>? TeamIds);
public sealed record LockRosterRequest(Guid? SeasonId, int? ExpectedRevision);

public static class RosterDraftEndpoints
{
    // The acting coach is always the signed-in user; nothing in a request names a coach or a membership.
    public static IEndpointRouteBuilder MapRosterDraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var roster = endpoints.MapGroup("/api/coach/roster").RequireAuthorization("CoachOnly");
        roster.MapGet("", async (ClaimsPrincipal principal, ICoachRosterService service, CancellationToken cancellationToken) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var coach)
                ? ToResult(await service.GetAsync(new UserId(coach), cancellationToken))
                : Results.Unauthorized());
        roster.MapPut("", async (SaveRosterRequest request, ClaimsPrincipal principal, ICoachRosterService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var coach)) return Results.Unauthorized();
            if (request.SeasonId is not { } seasonId || request.ExpectedRevision is not { } revision || request.TeamIds is null)
                return Results.BadRequest(new Error("roster.request", "seasonId, expectedRevision and teamIds are required."));
            if (revision < 0) return Results.BadRequest(new Error("roster.request", "expectedRevision cannot be negative."));
            return ToResult(await service.SaveAsync(new UserId(coach), new SaveRoster(seasonId, revision, request.TeamIds), cancellationToken));
        });
        roster.MapPost("/lock", async (LockRosterRequest request, ClaimsPrincipal principal, ICoachRosterService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var coach)) return Results.Unauthorized();
            if (request.SeasonId is not { } seasonId || request.ExpectedRevision is not { } revision)
                return Results.BadRequest(new Error("roster.request", "seasonId and expectedRevision are required."));
            if (revision < 0) return Results.BadRequest(new Error("roster.request", "expectedRevision cannot be negative."));
            return ToResult(await service.LockAsync(new UserId(coach), new LockRoster(seasonId, revision), cancellationToken));
        });
        return endpoints;
    }

    private static IResult ToResult(Result<RosterDesk> result) => result switch
    {
        { IsSuccess: true } => Results.Ok(result.Value),
        { Error.Code: "roster.count" or "roster.duplicate" or "roster.team" } => Results.BadRequest(result.Error),
        _ => Results.Conflict(result.Error)
    };
}
