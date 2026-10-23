using Swan.Core.Common;
using Swan.Services.Picks;
using System.Security.Claims;

namespace Swan.Api.Endpoints.Admin;

public static class WeekPickEndpoints
{
    public static IEndpointRouteBuilder MapWeekPickEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/weeks/{weekId:guid}/picks", async (Guid weekId, IAdminWeekPickService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(new WeekId(weekId), cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequireAuthorization("Admin");
        endpoints.MapPut("/api/admin/weeks/{weekId:guid}/picks/{membershipId:guid}", async (Guid weekId, Guid membershipId, AdminPickOverride? request, ClaimsPrincipal principal, IAdminWeekPickService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            if (request is null) return Results.BadRequest(new Error("pick.override.request", "teamIds, dogTeamIds, and reason are required."));
            var result = await service.OverrideAsync(new UserId(actor), new WeekId(weekId), new MembershipId(membershipId), request, cancellationToken);
            return result switch
            {
                { IsSuccess: true } => Results.Ok(result.Value),
                { Error.Code: "week.missing" or "membership.missing" or "roster.missing" } => Results.NotFound(result.Error),
                { Error.Code: "pick.override.reason" or "pick.invalid" or "dog.invalid" } => Results.BadRequest(result.Error),
                _ => Results.Conflict(result.Error)
            };
        }).RequireAuthorization("Admin");
        return endpoints;
    }
}
