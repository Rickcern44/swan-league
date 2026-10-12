using Swan.Core.Common;
using Swan.Core.Picks;
using Swan.Core.Scoring;
using Xunit;

namespace Swan.Core.Tests;

public sealed class RegularScoringPolicyTests
{
    [Fact]
    public void WinsScoreTheirPointValueWhileLossesAndTiesScoreZero()
    {
        var (ballot, games) = BallotWithThreeGames(GameWinner.Home, GameWinner.Away, GameWinner.Tie);
        var result = RegularScoringPolicy.Recompute(ballot, games, new());
        Assert.Equal(new[] { 1, 0, 0 }, result.Lines.Where(line => line.Source == ScoreSource.TeamPick).Select(line => line.Points));
        Assert.All(result.Lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Reference)));
    }

    [Fact]
    public void PerfectWeekAwardsThreeAndFlagsGoingPerfectButTieDoesNot()
    {
        var (ballot, games) = BallotWithThreeGames(GameWinner.Home, GameWinner.Home, GameWinner.Home);
        var perfect = RegularScoringPolicy.Recompute(ballot, games, new());
        Assert.True(perfect.GoingPerfect); Assert.Contains(perfect.Lines, line => line.Source == ScoreSource.PerfectWeek && line.Points == 3);
        var (_, ties) = BallotWithThreeGames(GameWinner.Home, GameWinner.Home, GameWinner.Tie);
        Assert.False(RegularScoringPolicy.Recompute(ballot, ties, new()).GoingPerfect);
    }

    [Fact]
    public void DogRoundsSpreadUpAndSettingsControlMainPoolAndCollisions()
    {
        var team = new TeamId(Guid.NewGuid()); var game = new GameId(Guid.NewGuid()); var now = DateTimeOffset.UtcNow;
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()), [team]);
        ballot.AddPick(new TeamPick(team, game, now.AddHours(1)), now); ballot.AddDog(new DogPick(team, game, now.AddHours(1)), false, now);
        var games = new[] { new ScoredGame(game, team, new TeamId(Guid.NewGuid()), new GameResult(21, 10), new Spread(2.5m)) };
        Assert.Equal(3, RegularScoringPolicy.Recompute(ballot, games, new(true, true)).Lines.Single(line => line.Source == ScoreSource.DogPick).Points);
        Assert.Equal(0, RegularScoringPolicy.Recompute(ballot, games, new(true, false)).Lines.Single(line => line.Source == ScoreSource.DogPick).Points);
    }

    [Fact]
    public void RecomputeIsIdempotentAndIncompleteBallotScoresOnlyPresentPick()
    {
        var (ballot, games) = BallotWithThreeGames(GameWinner.Home, GameWinner.Home, GameWinner.Home, 1);
        var first = RegularScoringPolicy.Recompute(ballot, games, new()); var second = RegularScoringPolicy.Recompute(ballot, games, new());
        Assert.Equal(first.Lines, second.Lines); Assert.Single(first.Lines); Assert.False(first.GoingPerfect); Assert.False(first.GoingOhFor);
    }

    private static (Ballot Ballot, ScoredGame[] Games) BallotWithThreeGames(GameWinner one, GameWinner two, GameWinner three, int count = 3)
    {
        var now = DateTimeOffset.UtcNow; var teams = Enumerable.Range(0, 3).Select(_ => new TeamId(Guid.NewGuid())).ToArray();
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()), teams);
        var outcomes = new[] { one, two, three }; var games = new List<ScoredGame>();
        for (var index = 0; index < count; index++) { var game = new GameId(Guid.NewGuid()); ballot.AddPick(new TeamPick(teams[index], game, now.AddHours(1), PointValue: index + 1), now); games.Add(new ScoredGame(game, teams[index], new TeamId(Guid.NewGuid()), outcomes[index] == GameWinner.Home ? new GameResult(1, 0) : outcomes[index] == GameWinner.Away ? new GameResult(0, 1) : new GameResult(1, 1), null)); }
        return (ballot, games.ToArray());
    }
}
