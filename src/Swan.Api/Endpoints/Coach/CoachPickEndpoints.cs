using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Picks;

namespace Swan.Api.Endpoints.Coach;

// Every field is optional in the contract so a missing one is a readable 400 instead of a model-binding failure.
public sealed record SavePicksRequest(Guid? WeekId, int? ExpectedRevision, IReadOnlyList<Guid>? TeamIds, IReadOnlyList<Guid>? DogTeamIds);

public static class CoachPickEndpoints
{
    // The acting coach is always the signed-in user; nothing in a request names a coach, membership or ballot.
    public static IEndpointRouteBuilder MapCoachPickEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var picks = endpoints.MapGroup("/api/coach/picks").RequireAuthorization("CoachOnly");
        picks.MapGet("/current", async (ClaimsPrincipal principal, ICoachPickService service, CancellationToken cancellationToken) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var coach)
                ? ToResult(await service.GetAsync(new UserId(coach), cancellationToken))
                : Results.Unauthorized());
        picks.MapPut("/current", async (SavePicksRequest request, ClaimsPrincipal principal, ICoachPickService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var coach)) return Results.Unauthorized();
            if (request.WeekId is not { } weekId || request.TeamIds is null || request.DogTeamIds is null || request.ExpectedRevision is not { } revision)
                return Results.BadRequest(new Error("pick.request", "weekId, expectedRevision, teamIds and dogTeamIds are required."));
            if (revision < 0) return Results.BadRequest(new Error("pick.request", "expectedRevision cannot be negative."));
            return ToResult(await service.SaveAsync(new UserId(coach), new SavePicks(weekId, revision, request.TeamIds, request.DogTeamIds), cancellationToken));
        });
        return endpoints;
    }

    private static IResult ToResult(Result<PickDesk> result) => result switch
    {
        { IsSuccess: true } => Results.Ok(result.Value),
        { Error.Code: "pick.invalid" or "dog.invalid" } => Results.BadRequest(result.Error),
        _ => Results.Conflict(result.Error)
    };
}
