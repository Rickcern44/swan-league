using System.Security.Claims;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Services.Platform;

namespace Swan.Api.Endpoints.Admin;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit", GetAudit).RequireAuthorization("Admin");
        endpoints.MapGet("/api/admin/ping", RecordAdminPing).RequireAuthorization("Admin");
        return endpoints;
    }

    private static async Task<IResult> GetAudit(
        Guid? actor,
        string? action,
        Guid? weekId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IAuditService audit,
        CancellationToken cancellationToken) => Results.Ok(await audit.QueryAsync(new AuditFilter(actor is { } actorId ? new UserId(actorId) : null, action, from, to, weekId is { } requestedWeekId ? new WeekId(requestedWeekId) : null), cancellationToken));

    private static async Task<IResult> RecordAdminPing(
        ClaimsPrincipal principal,
        IAuditService audit,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        await audit.AppendAsync(new UserId(actor), "admin.ping", "admin-ping", reason: "Administrative endpoint check", cancellationToken: cancellationToken);
        return Results.Ok(new { message = "admin-pong" });
    }
}
