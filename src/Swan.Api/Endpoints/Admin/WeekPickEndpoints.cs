using Swan.Core.Common;
using Swan.Services.Picks;

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
        return endpoints;
    }
}
