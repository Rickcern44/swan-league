using Swan.Services.Schedule;

namespace Swan.Api.Endpoints.Admin;

public sealed record DashboardSeasonResponse(Guid Id, string Name, int Year, bool IsSimulation);
public sealed record DashboardIncompleteSubmissionResponse(Guid MembershipId, string Name, int Starts, int StartLimit, int Dogs, int DogLimit);
public sealed record DashboardSubmissionsResponse(int Complete, int Of, IReadOnlyList<DashboardIncompleteSubmissionResponse> Incomplete);
public sealed record DashboardWeekResponse(Guid Id, int Number, string Kind, string State, int Games, int IncludedGames, int LinesApproved, int ResultsConfirmed, bool IsDoubleDogWeek, DashboardSubmissionsResponse? Submissions);
public sealed record DashboardAttentionResponse(string Kind, string Title, string Reason, Guid WeekId, int WeekNumber, string WeekState, int? Count, int? Of, bool NeedsDecision);
public sealed record DashboardMoneyResponse(int BuyInsPaid, int BuyInsDue, int FinePoolCents, int PendingFines, int PendingFinesCents);
public sealed record DashboardJobResponse(bool Healthy, DateTimeOffset? LastScoreSync, DateTimeOffset? NextReminder, int Failures);
public sealed record DashboardResponse(DashboardSeasonResponse? Season, DashboardWeekResponse? CurrentWeek, IReadOnlyList<DashboardAttentionResponse> Attention, bool CaughtUp, DashboardMoneyResponse? Money, DashboardJobResponse? Jobs);

public static class DashboardEndpoints
{
    // Read-only: one call returns the live season, its current week and the ranked attention list, so the browser never scans for it.
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/dashboard", async (IAdminDashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(ToResponse((await service.GetAsync(cancellationToken)).Value!))).RequireAuthorization("Admin");
        return endpoints;
    }

    private static DashboardResponse ToResponse(AdminDashboard dashboard) => new(
        dashboard.Season is { } season ? new DashboardSeasonResponse(season.Id.Value, season.Name, season.Year, season.IsSimulation) : null,
        dashboard.CurrentWeek is { } week ? new DashboardWeekResponse(week.Id.Value, week.Number, week.Kind.ToString(), week.State.ToString(), week.Games, week.IncludedGames, week.LinesApproved, week.ResultsConfirmed, week.IsDoubleDogWeek, week.Submissions is { } submissions ? new DashboardSubmissionsResponse(submissions.Complete, submissions.Of, submissions.Incomplete.Select(item => new DashboardIncompleteSubmissionResponse(item.MembershipId.Value, item.Name, item.Starts, item.StartLimit, item.Dogs, item.DogLimit)).ToList()) : null) : null,
        dashboard.Attention.Select(item => new DashboardAttentionResponse(item.Kind, item.Title, item.Reason, item.WeekId.Value, item.WeekNumber, item.WeekState.ToString(), item.Count, item.Of, item.NeedsDecision)).ToList(),
        dashboard.CaughtUp,
        dashboard.Money is { } money ? new DashboardMoneyResponse(money.BuyInsPaid, money.BuyInsDue, money.FinePoolCents, money.PendingFines, money.PendingFinesCents) : null,
        dashboard.Jobs is { } jobs ? new DashboardJobResponse(jobs.Healthy, jobs.LastScoreSync, jobs.NextReminder, jobs.Failures) : null);
}
