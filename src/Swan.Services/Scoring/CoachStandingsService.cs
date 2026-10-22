using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Scoring;
using Swan.Data;

namespace Swan.Services.Scoring;

public sealed record CoachStanding(Guid MembershipId, int Rank, string Name, int Points, int Wins, bool IsYou, int? Movement);
public sealed record CoachStandingWithLeader(Guid MembershipId, int Rank, string Name, int Points, int Wins, bool IsYou, int? Movement, int BehindLeader);
public sealed record CoachStandings(string State, string? SeasonName, string Pool, int? ThroughWeek, int CoachCount, IReadOnlyList<CoachStanding> Rows, CoachStandingWithLeader? You);

public interface ICoachStandingsService
{
    Task<CoachStandings> GetAsync(UserId coach, StandingPool pool, CancellationToken cancellationToken = default);
}

public sealed class CoachStandingsService(SwanDbContext database) : ICoachStandingsService
{
    public async Task<CoachStandings> GetAsync(UserId coach, StandingPool pool, CancellationToken cancellationToken = default)
    {
        var live = (await database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Live).ToListAsync(cancellationToken))
            .OrderByDescending(season => season.Year).ThenBy(season => season.Name, StringComparer.Ordinal).ThenBy(season => season.Id.Value).ToList();
        var memberships = await database.Memberships.AsNoTracking().Where(item => item.UserId == coach && item.Role == MembershipRole.Coach && item.IsActive).ToListAsync(cancellationToken);
        var membership = live.Select(season => memberships.FirstOrDefault(item => item.SeasonId == season.Id)).FirstOrDefault(item => item is not null);
        if (membership is null) return new("no-membership", null, pool.ToString(), null, 0, [], null);

        var season = live.First(item => item.Id == membership.SeasonId);
        var coaches = await database.Memberships.AsNoTracking().Where(item => item.SeasonId == season.Id && item.Role == MembershipRole.Coach && item.IsActive).ToListAsync(cancellationToken);
        var users = await database.Users.AsNoTracking().Where(user => coaches.Select(item => item.UserId).Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);
        var weeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == season.Id).ToDictionaryAsync(week => week.Id, cancellationToken);
        var scores = await database.WeekScores.AsNoTracking().Where(score => score.SeasonId == season.Id).ToListAsync(cancellationToken);
        var scored = scores.Select(score => weeks.GetValueOrDefault(score.WeekId)).Where(week => week is not null).Select(week => week!).GroupBy(week => week.Id).Select(group => group.First()).OrderBy(week => week.Number).ToList();
        if (scored.Count == 0) return new("no-scores", season.Name, pool.ToString(), null, coaches.Count, [], null);

        var current = Rank(coaches, users, scores, weeks, pool, scored[^1].Number, membership.Id);
        var previous = scored.Count > 1 ? Rank(coaches, users, scores, weeks, pool, scored[^2].Number, membership.Id).ToDictionary(row => row.MembershipId) : null;
        var rows = current.Select(row => row with { Movement = previous is null ? null : previous[row.MembershipId].Rank - row.Rank }).ToList();
        var you = rows.Single(row => row.IsYou);
        return new("ready", season.Name, pool.ToString(), scored[^1].Number, coaches.Count, rows, new(you.MembershipId, you.Rank, you.Name, you.Points, you.Wins, you.IsYou, you.Movement, rows[0].Points - you.Points));
    }

    private static List<CoachStanding> Rank(IReadOnlyList<Membership> coaches, IReadOnlyDictionary<UserId, string> users, IReadOnlyList<WeekScore> scores, IReadOnlyDictionary<WeekId, Swan.Core.Schedule.Week> weeks, StandingPool pool, int throughWeek, MembershipId viewer)
    {
        var totals = coaches.Select(coach =>
        {
            var lines = scores.Where(score => score.MembershipId == coach.Id && weeks.TryGetValue(score.WeekId, out var week) && week.Number <= throughWeek)
                .SelectMany(score => score.Lines.Select(line => (line, weeks[score.WeekId].Type.Kind))).Where(item => Include(item.line, item.Kind, pool)).ToList();
            return (coach, Points: lines.Sum(item => item.line.Points), Wins: lines.Count(item => item.line.Source == ScoreSource.TeamPick && item.line.Points > 0));
        }).OrderByDescending(item => item.Points).ThenBy(item => item.coach.Id.Value).ToList();
        return totals.Select((item, index) => new CoachStanding(item.coach.Id.Value, index > 0 && item.Points == totals[index - 1].Points ? totals.Take(index).Count(previous => previous.Points > item.Points) + 1 : index + 1, users.GetValueOrDefault(item.coach.UserId) ?? "Unknown coach", item.Points, item.Wins, item.coach.Id == viewer, null)).ToList();
    }

    private static bool Include(ScoreLine line, Swan.Core.Schedule.WeekKind kind, StandingPool pool) => pool switch
    {
        StandingPool.Dog => line.Source == ScoreSource.DogPick,
        StandingPool.Main => line.Source != ScoreSource.DogPick,
        StandingPool.RegularSeason => kind == Swan.Core.Schedule.WeekKind.Regular && line.Source != ScoreSource.DogPick,
        StandingPool.Playoff => kind is Swan.Core.Schedule.WeekKind.Playoff or Swan.Core.Schedule.WeekKind.SuperBowl && line.Source != ScoreSource.DogPick,
        _ => false
    };
}
