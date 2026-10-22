using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Data;

namespace Swan.Services.Schedule;

public sealed record DashboardSeason(SeasonId Id, string Name, int Year, bool IsSimulation);
public sealed record DashboardIncompleteSubmission(MembershipId MembershipId, string Name, int Starts, int StartLimit, int Dogs, int DogLimit);
public sealed record DashboardSubmissions(int Complete, int Of, IReadOnlyList<DashboardIncompleteSubmission> Incomplete);
public sealed record DashboardWeek(WeekId Id, int Number, WeekKind Kind, WeekState State, int Games, int IncludedGames, int LinesApproved, int ResultsConfirmed, bool IsDoubleDogWeek, DashboardSubmissions? Submissions = null);

// The administrator's home: the live season, its current week and the ranked list of what needs attention. No season-in-play means Season is null and nothing is invented.
public sealed record DashboardMoneySummary(int BuyInsPaid, int BuyInsDue, int FinePoolCents, int PendingFines, int PendingFinesCents);
public sealed record DashboardJobSummary(bool Healthy, DateTimeOffset? LastScoreSync, DateTimeOffset? NextReminder, int Failures);
public sealed record AdminDashboard(DashboardSeason? Season, DashboardWeek? CurrentWeek, IReadOnlyList<AttentionItem> Attention, bool CaughtUp, DashboardMoneySummary? Money = null, DashboardJobSummary? Jobs = null);

public interface IAdminDashboardService
{
    Task<Result<AdminDashboard>> GetAsync(CancellationToken cancellationToken = default);
}

// Read-only: no provider calls and no writes. If several seasons are Live, the newest Year wins, then the name, then the id, so the answer never depends on row order.
public sealed class AdminDashboardService(SwanDbContext database) : IAdminDashboardService
{
    public async Task<Result<AdminDashboard>> GetAsync(CancellationToken cancellationToken = default)
    {
        var season = (await database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Live).ToListAsync(cancellationToken))
            .OrderByDescending(season => season.Year).ThenBy(season => season.Name, StringComparer.Ordinal).ThenBy(season => season.Id.Value).FirstOrDefault();
        if (season is null) return Result<AdminDashboard>.Success(new AdminDashboard(null, null, [], false));

        var weeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == season.Id).ToListAsync(cancellationToken);
        var snapshots = weeks.Select(week => ToSnapshot(week, season)).ToList();
        var attention = AdminAttention.Derive(snapshots);
        var current = attention.CurrentWeek is null ? null : await BuildWeekAsync(attention.CurrentWeek, season, cancellationToken);
        return Result<AdminDashboard>.Success(new AdminDashboard(new DashboardSeason(season.Id, season.Name, season.Year, season.IsSimulation), current, attention.Items, attention.CaughtUp));
    }

    private async Task<DashboardWeek> BuildWeekAsync(WeekSnapshot snapshot, Season season, CancellationToken cancellationToken)
    {
        var coaches = await database.Memberships.AsNoTracking().Where(member => member.SeasonId == season.Id && member.Role == MembershipRole.Coach && member.IsActive).ToListAsync(cancellationToken);
        var names = await database.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);
        var ballots = await database.Ballots.AsNoTracking().Where(ballot => ballot.WeekId == snapshot.Id).ToDictionaryAsync(ballot => ballot.MembershipId, cancellationToken);
        var dogLimit = snapshot.IsDoubleDogWeek ? 2 : 1;
        var incomplete = coaches.Select(member =>
        {
            ballots.TryGetValue(member.Id, out var ballot);
            return new DashboardIncompleteSubmission(member.Id, names.GetValueOrDefault(member.UserId) ?? "Unknown coach", ballot?.Picks.Count ?? 0, 3, ballot?.Dogs.Count ?? 0, dogLimit);
        }).Where(item => item.Starts != item.StartLimit || item.Dogs != item.DogLimit)
            .OrderBy(item => item.Starts + item.Dogs).ThenBy(item => item.Name, StringComparer.Ordinal).ToList();
        var submissions = new DashboardSubmissions(coaches.Count - incomplete.Count, coaches.Count, incomplete);
        return new(snapshot.Id, snapshot.Number, snapshot.Kind, snapshot.State, snapshot.Games, snapshot.IncludedGames, snapshot.LinesApproved, snapshot.ResultsConfirmed, snapshot.IsDoubleDogWeek, submissions);
    }

    private static WeekSnapshot ToSnapshot(Week week, Season season)
    {
        var included = week.Games.Where(game => !game.Excluded).ToList();
        return new WeekSnapshot(week.Id, week.Number, week.State, week.Type.Kind, week.Games.Count, included.Count, included.Count(game => game.ApprovedLine is not null), included.Count(game => game.Result is not null), season.DoubleDogWeekNumbers.Contains(week.Number), included.Count(game => game.ApprovedLine is not null && game.HasUnapprovedLine));
    }
}
