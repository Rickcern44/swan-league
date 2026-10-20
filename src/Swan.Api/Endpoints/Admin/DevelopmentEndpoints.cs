using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Development;

namespace Swan.Api.Endpoints.Admin;

// Local-only tools. Program maps these only when the environment is Development, so in any other environment the route does not exist.
public static class DevelopmentEndpoints
{
    public static IEndpointRouteBuilder MapDevelopmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/development/reset-season-data", ResetSeasonData).RequireAuthorization("Admin");
        endpoints.MapGet("/api/admin/development/clock", GetClock).RequireAuthorization("Admin");
        endpoints.MapPost("/api/admin/development/clock/advance", AdvanceClock).RequireAuthorization("Admin");
        return endpoints;
    }

    private sealed record AdvanceClockRequest(int Minutes);

    private static IResult GetClock(DevelopmentClock clock) => Results.Ok(new { utcNow = clock.UtcNow });

    private static IResult AdvanceClock(AdvanceClockRequest request, DevelopmentClock clock) =>
        request.Minutes is < 0 or > 60 * 24 * 14
            ? Results.BadRequest(new Error("development.clock", "Advance between 0 and 20,160 minutes."))
            : Results.Ok(new { utcNow = clock.Advance(TimeSpan.FromMinutes(request.Minutes)) });

    private static async Task<IResult> ResetSeasonData(ClaimsPrincipal principal, DevelopmentResetService reset, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        var result = await reset.ResetSeasonDataAsync(new UserId(actor), cancellationToken);
        return result.IsSuccess ? Results.Ok(new { removed = result.Value!.Removed }) : Results.Conflict(result.Error);
    }
}
