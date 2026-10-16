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
        return endpoints;
    }

    private static async Task<IResult> ResetSeasonData(ClaimsPrincipal principal, DevelopmentResetService reset, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        var result = await reset.ResetSeasonDataAsync(new UserId(actor), cancellationToken);
        return result.IsSuccess ? Results.Ok(new { removed = result.Value!.Removed }) : Results.Conflict(result.Error);
    }
}
