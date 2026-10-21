using System.Security.Claims;
using Swan.Core.Common;
using Swan.Services.Picks;

namespace Swan.Api.Endpoints.Coach;

public sealed record CorrectRosterRequest(Guid? SeasonId, Guid? MembershipId, int? ExpectedRevision, Guid? RemoveTeamId, Guid? AddTeamId, string? Reason);
public sealed record StartRosterDraftRequest(Guid? SeasonId, string? RosterDraftDeadlineEastern);

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
        draft.MapPost("/start", async (StartRosterDraftRequest request, ClaimsPrincipal principal, ICommissionerRosterService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            if (request.SeasonId is not { } seasonId || !TryParseEastern(request.RosterDraftDeadlineEastern, out var deadline))
                return Results.BadRequest(new Error("season.draft-deadline", "seasonId and a roster draft deadline in Eastern Time are required."));
            return ToResult(await service.StartAsync(new UserId(actor), new StartRosterDraft(seasonId, deadline), cancellationToken));
        });
        draft.MapPost("/reminders", async (ClaimsPrincipal principal, ICommissionerRosterService service, CancellationToken cancellationToken) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
                ? ToResult(await service.QueueRemindersAsync(new UserId(actor), cancellationToken))
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
        { Error.Code: "season.draft-deadline" or "roster.reason" or "roster.team" or "roster.correction.invalid" or "roster.duplicate" or "roster.count" } => Results.BadRequest(result.Error),
        _ => Results.Conflict(result.Error)
    };

    private static IResult ToResult(Result<ReminderQueueResult> result) => result switch
    {
        { IsSuccess: true } => Results.Ok(result.Value),
        { Error.Code: "commissioner.required" } => Results.Json(result.Error, statusCode: StatusCodes.Status403Forbidden),
        { Error.Code: "season.missing" } => Results.NotFound(result.Error),
        { Error.Code: "roster.draft-closed" } => Results.Conflict(result.Error),
        _ => Results.Conflict(result.Error)
    };

    private static bool TryParseEastern(string? text, out DateTime value)
    {
        value = default;
        return !string.IsNullOrWhiteSpace(text)
            && DateTime.TryParseExact(text.Trim(), ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out value);
    }
}
