using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Scoring;

namespace Swan.Api.Endpoints.Coach;

public static class CoachStandingsEndpoints
{
    public static IEndpointRouteBuilder MapCoachStandingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var standings = endpoints.MapGroup("/api/coach/standings").RequireAuthorization("CoachOnly");
        standings.MapGet("", async (string? pool, ClaimsPrincipal principal, ICoachStandingsService service, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<StandingPool>(pool ?? nameof(StandingPool.Main), true, out var parsed)) return Results.BadRequest(new Error("standings.pool", "pool must be Main, Dog, RegularSeason or Playoff."));
            return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var coach)
                ? Results.Ok(await service.GetAsync(new UserId(coach), parsed, cancellationToken))
                : Results.Unauthorized();
        });
        return endpoints;
    }
}
