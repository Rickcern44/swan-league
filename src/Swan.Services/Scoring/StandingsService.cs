using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
using Swan.Data;

namespace Swan.Services.Scoring;

public enum StandingPool { Main, Dog, RegularSeason, Playoff }
public sealed record Standing(MembershipId MembershipId, int Rank, int Points, int Wins, bool TiebreakUnbroken = true);

public sealed class StandingsService(SwanDbContext database)
{
    public async Task<IReadOnlyList<Standing>> GetAsync(
        SeasonId seasonId,
        StandingPool pool,
        WeekId? weekId = null,
        bool dogsContributeToMainPool = false,
        CancellationToken cancellationToken = default)
    {
        var scores = await database.WeekScores
            .Where(score => score.SeasonId == seasonId && (weekId == null || score.WeekId == weekId))
            .ToListAsync(cancellationToken);
        var weekKinds = await database.Weeks
            .Where(week => week.SeasonId == seasonId && scores.Select(score => score.WeekId).Contains(week.Id))
            .ToDictionaryAsync(week => week.Id, week => week.Type.Kind, cancellationToken);

        var totals = scores
            .GroupBy(score => score.MembershipId)
            .Select(group => new
            {
                Membership = group.Key,
                Lines = group.SelectMany(score => score.Lines.Select(line => new { Line = line, WeekKind = weekKinds[score.WeekId] })),
            })
            .Select(value => new
            {
                value.Membership,
                Points = value.Lines.Where(value => Include(value.Line, value.WeekKind, pool, dogsContributeToMainPool)).Sum(value => value.Line.Points),
                Wins = value.Lines.Count(value => Include(value.Line, value.WeekKind, pool, dogsContributeToMainPool) && value.Line.Source == ScoreSource.TeamPick && value.Line.Points > 0),
            })
            .OrderByDescending(value => value.Points)
            .ThenBy(value => value.Membership.Value)
            .ToList();
        return totals.Select((value, index) => new Standing(value.Membership, index > 0 && value.Points == totals[index - 1].Points ? totals.Take(index).Count(previous => previous.Points > value.Points) + 1 : index + 1, value.Points, value.Wins)).ToList();
    }

    private static bool Include(ScoreLine line, WeekKind weekKind, StandingPool pool, bool dogsContributeToMainPool) => pool switch
    {
        StandingPool.Dog => line.Source == ScoreSource.DogPick,
        StandingPool.Main => line.Source != ScoreSource.DogPick || dogsContributeToMainPool,
        StandingPool.RegularSeason => weekKind == WeekKind.Regular && line.Source != ScoreSource.DogPick,
        StandingPool.Playoff => weekKind is WeekKind.Playoff or WeekKind.SuperBowl && line.Source != ScoreSource.DogPick,
        _ => false
    };
}
