using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Data;

namespace Swan.Services.Picks;

public sealed record PickSubmission(MembershipId MembershipId, string Name, int Starts, int StartLimit, int Dogs, int DogLimit)
{
    public bool Complete => Starts == StartLimit && Dogs == DogLimit;
}
public sealed record AdminPick(Guid TeamId, string Team, Guid GameId, int? PointValue, decimal? Spread, bool Locked, string? Result);
public sealed record AdminCoachPicks(Guid MembershipId, string Name, IReadOnlyList<AdminPick> Starts, IReadOnlyList<AdminPick> Dogs, bool Complete);
public sealed record AdminWeekPicks(IReadOnlyList<AdminCoachPicks> Coaches);

public interface IAdminWeekPickService
{
    Task<Result<AdminWeekPicks>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default);
}

public sealed class AdminWeekPickService(SwanDbContext database, IClock clock) : IAdminWeekPickService
{
    public async Task<Result<AdminWeekPicks>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default)
    {
        var week = await database.Weeks.AsNoTracking().SingleOrDefaultAsync(value => value.Id == weekId, cancellationToken);
        if (week is null) return Result<AdminWeekPicks>.Failure("week.missing", "Week does not exist.");
        var memberships = await database.Memberships.AsNoTracking().Where(value => value.SeasonId == week.SeasonId && value.Role == MembershipRole.Coach && value.IsActive).ToListAsync(cancellationToken);
        var users = await database.Users.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var ballots = await database.Ballots.AsNoTracking().Where(value => value.WeekId == weekId).ToDictionaryAsync(value => value.MembershipId, cancellationToken);
        var teams = await database.Teams.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var games = week.Games.ToDictionary(value => value.Id);
        var doubleDog = (await database.Seasons.AsNoTracking().SingleAsync(value => value.Id == week.SeasonId, cancellationToken)).DoubleDogWeekNumbers.Contains(week.Number);
        AdminPick Pick(TeamId teamId, GameId gameId, int? points, decimal? spread, DateTimeOffset lockTime)
        {
            games.TryGetValue(gameId, out var game);
            var result = week.State == WeekState.Final && game?.Result is { } score ? $"{score.HomeScore}-{score.AwayScore}" : null;
            return new(teamId.Value, teams.GetValueOrDefault(teamId) ?? "Unknown team", gameId.Value, points, spread, !LockPolicy.CanChange(lockTime, clock.UtcNow), result);
        }
        var coaches = memberships.Select(member =>
        {
            ballots.TryGetValue(member.Id, out var ballot);
            var starts = ballot?.Picks.Select(value => Pick(value.TeamId, value.GameId, value.PointValue, null, value.LockTime)).ToList() ?? [];
            var dogs = ballot?.Dogs.Select(value => Pick(value.TeamId, value.GameId, null, games.GetValueOrDefault(value.GameId)?.ApprovedLine?.Value, value.LockTime)).ToList() ?? [];
            return new AdminCoachPicks(member.Id.Value, users.GetValueOrDefault(member.UserId) ?? "Unknown coach", starts, dogs, starts.Count == 3 && dogs.Count == (doubleDog ? 2 : 1));
        }).OrderBy(value => value.Name, StringComparer.Ordinal).ToList();
        return Result<AdminWeekPicks>.Success(new(coaches));
    }
}
