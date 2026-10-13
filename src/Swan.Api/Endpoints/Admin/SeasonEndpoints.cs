using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.League;

namespace Swan.Api.Endpoints.Admin;

public sealed record CreateSeasonRequest(int Year, string? Name, bool IsSimulation = false);

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
        return endpoints;
    }
}
