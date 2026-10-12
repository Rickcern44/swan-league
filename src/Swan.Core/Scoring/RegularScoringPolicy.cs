using Swan.Core.Common;
using Swan.Core.Picks;

namespace Swan.Core.Scoring;

public sealed record ScoredGame(GameId GameId, TeamId HomeTeamId, TeamId AwayTeamId, GameResult? Result, Spread? ApprovedSpread);
public sealed record RegularScoringSettings(bool DogsContributeToMainPool = false, bool TeamMayAlsoBeDog = true);
public sealed record RegularScoreResult(IReadOnlyList<ScoreLine> Lines, bool GoingPerfect, bool GoingOhFor);

public static class RegularScoringPolicy
{
    public static RegularScoreResult Recompute(Ballot ballot, IEnumerable<ScoredGame> games, RegularScoringSettings settings)
    {
        var byGame = games.ToDictionary(game => game.GameId);
        var lines = new List<ScoreLine>();
        var teamWins = 0;

        foreach (var pick in ballot.Picks)
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

        foreach (var dog in ballot.Dogs)
        {
            if (!byGame.TryGetValue(dog.GameId, out var game) || game.Result is null || game.ApprovedSpread is null) continue;
            var won = game.Result.Winner switch
            {
                GameWinner.Home => game.HomeTeamId == dog.TeamId,
                GameWinner.Away => game.AwayTeamId == dog.TeamId,
                _ => false
            };
            var collisionDisallowed = !settings.TeamMayAlsoBeDog && ballot.Picks.Any(pick => pick.TeamId == dog.TeamId);
            var points = won && !collisionDisallowed ? decimal.ToInt32(decimal.Ceiling(Math.Abs(game.ApprovedSpread.Value))) : 0;
            lines.Add(new ScoreLine(dog.TeamId, settings.DogsContributeToMainPool ? points : 0, ScoreSource.DogPick, dog.GameId.Value.ToString()));
        }

        if (ballot.Picks.Count == 3 && teamWins == 3)
            lines.Add(new ScoreLine(null, 3, ScoreSource.PerfectWeek, ballot.Id.Value.ToString()));

        return new RegularScoreResult(lines, ballot.Picks.Count == 3 && teamWins == 3, ballot.Picks.Count == 3 && teamWins == 0);
    }
}
