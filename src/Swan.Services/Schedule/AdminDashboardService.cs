using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Data;

namespace Swan.Services.Schedule;

public sealed record DashboardSeason(SeasonId Id, string Name, int Year, bool IsSimulation);
public sealed record DashboardWeek(WeekId Id, int Number, WeekKind Kind, WeekState State, int Games, int IncludedGames, int LinesApproved, int ResultsConfirmed, bool IsDoubleDogWeek);

// The administrator's home: the live season, its current week and the ranked list of what needs attention. No season-in-play means Season is null and nothing is invented.
public sealed record AdminDashboard(DashboardSeason? Season, DashboardWeek? CurrentWeek, IReadOnlyList<AttentionItem> Attention, bool CaughtUp);

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
        var current = attention.CurrentWeek is null ? null : new DashboardWeek(attention.CurrentWeek.Id, attention.CurrentWeek.Number, attention.CurrentWeek.Kind, attention.CurrentWeek.State, attention.CurrentWeek.Games, attention.CurrentWeek.IncludedGames, attention.CurrentWeek.LinesApproved, attention.CurrentWeek.ResultsConfirmed, attention.CurrentWeek.IsDoubleDogWeek);
        return Result<AdminDashboard>.Success(new AdminDashboard(new DashboardSeason(season.Id, season.Name, season.Year, season.IsSimulation), current, attention.Items, attention.CaughtUp));
    }

    private static WeekSnapshot ToSnapshot(Week week, Season season)
    {
        var included = week.Games.Where(game => !game.Excluded).ToList();
        return new WeekSnapshot(week.Id, week.Number, week.State, week.Type.Kind, week.Games.Count, included.Count, included.Count(game => game.ApprovedLine is not null), included.Count(game => game.Result is not null), season.DoubleDogWeekNumbers.Contains(week.Number), included.Count(game => game.ApprovedLine is not null && game.HasUnapprovedLine));
    }
}
