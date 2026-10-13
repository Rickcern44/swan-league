using Swan.Core.Common;

namespace Swan.Core.Scoring;

public sealed record ScoredGame(GameId GameId, TeamId HomeTeamId, TeamId AwayTeamId, GameResult? Result, Spread? ApprovedSpread);
public sealed record RegularScoringSettings(bool TeamMayAlsoBeDog = true);
public sealed record RegularScoreResult(IReadOnlyList<ScoreLine> Lines, bool GoingPerfect, bool GoingOhFor);

public static class RegularScoringPolicy
{
    public static RegularScoreResult Recompute(IScoringBallot ballot, IEnumerable<ScoredGame> games, RegularScoringSettings settings)
    {
        var byGame = games.ToDictionary(game => game.GameId);
        var lines = new List<ScoreLine>();
        var teamWins = 0;

        foreach (var pick in ballot.ScoringPicks)
        {
            if (!byGame.TryGetValue(pick.GameId, out var game) || game.Result is null) continue;
            var won = game.Result.Winner switch
            {
                GameWinner.Home => game.HomeTeamId == pick.TeamId,
                GameWinner.Away => game.AwayTeamId == pick.TeamId,
                _ => false
            };
            var points = won ? pick.PointValue : 0;
            lines.Add(new ScoreLine(pick.TeamId, points, ScoreSource.TeamPick, pick.GameId.Value.ToString()));
            if (won) teamWins++;
        }

        foreach (var dog in ballot.ScoringDogs)
        {
            if (!byGame.TryGetValue(dog.GameId, out var game) || game.Result is null || game.ApprovedSpread is null) continue;
            var won = game.Result.Winner switch
            {
                GameWinner.Home => game.HomeTeamId == dog.TeamId,
                GameWinner.Away => game.AwayTeamId == dog.TeamId,
                _ => false
            };
            var collisionDisallowed = !settings.TeamMayAlsoBeDog && ballot.ScoringPicks.Any(pick => pick.TeamId == dog.TeamId);
            var points = won && !collisionDisallowed ? decimal.ToInt32(decimal.Ceiling(Math.Abs(game.ApprovedSpread.Value))) : 0;
            lines.Add(new ScoreLine(dog.TeamId, points, ScoreSource.DogPick, dog.GameId.Value.ToString()));
        }

        if (ballot.ScoringPicks.Count == 3 && teamWins == 3)
            lines.Add(new ScoreLine(null, 3, ScoreSource.PerfectWeek, ballot.ScoringReference));

        return new RegularScoreResult(lines, ballot.ScoringPicks.Count == 3 && teamWins == 3, ballot.ScoringPicks.Count == 3 && teamWins == 0);
    }
}
