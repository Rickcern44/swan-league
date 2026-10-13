using System.Text.Json;
using Swan.Core.Common;
using Swan.Core.Picks;
using Swan.Core.Scoring;
using Xunit;

namespace Swan.Core.Tests;

public sealed class ScoringFixtureTests
{
    [Fact]
    public void JsonFixturesRunThroughRegularPolicy()
    {
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "regular-scoring.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var fixture in fixtures)
        {
            var now = DateTimeOffset.UtcNow; var teams = fixture.Points.Select(_ => new TeamId(Guid.NewGuid())).ToArray(); var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()), teams); var games = new List<ScoredGame>();
            for (var i = 0; i < teams.Length; i++) { var game = new GameId(Guid.NewGuid()); ballot.AddPick(new TeamPick(teams[i], game, now.AddHours(1), PointValue: fixture.Points[i]), now); if (fixture.Dog) ballot.AddDog(new DogPick(teams[i], game, now.AddHours(1)), true, now); games.Add(new ScoredGame(game, teams[i], new TeamId(Guid.NewGuid()), fixture.Outcomes[i] == "Home" ? new GameResult(1, 0) : fixture.Outcomes[i] == "Away" ? new GameResult(0, 1) : new GameResult(1, 1), fixture.Dog ? new Spread(fixture.Spread) : null)); }
            var result = RegularScoringPolicy.Recompute(ballot, games, new(fixture.TeamDog));
            Assert.Equal(fixture.Expected, result.Lines.Sum(line => line.Points));
        }
    }
    private sealed record Fixture(string Name, int[] Points, string[] Outcomes, int Expected, bool Dog = false, decimal Spread = 0, bool TeamDog = true);
}
