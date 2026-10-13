using Swan.Core.Common;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class SetupTeamFinishesTests
{
    private static readonly string[] Divisions = ["AFC East", "AFC North", "AFC South", "AFC West", "NFC East", "NFC North", "NFC South", "NFC West"];
    private static readonly SeasonId Season = new(Guid.NewGuid());

    private static List<Team> Catalog() => Divisions.SelectMany(division => Enumerable.Range(1, 4).Select(slot =>
        new Team(new TeamId(Guid.NewGuid()), $"{division} Team {slot}", null, DateTimeOffset.UtcNow, division))).ToList();

    private static List<TeamFinish> Valid(List<Team> teams) => teams.GroupBy(team => team.Division)
        .SelectMany(group => group.Select((team, index) => new TeamFinish(team.Id, index + 1))).ToList();

    [Fact]
    public void ACompleteValidSubmissionBuildsOneSeasonTeamPerTeamWithTheDivisionAndFinish()
    {
        var teams = Catalog();

        var built = SetupTeamFinishes.Create(Season, teams, Valid(teams));

        Assert.True(built.IsSuccess);
        Assert.Equal(32, built.Value!.Count);
        Assert.All(built.Value, seasonTeam => Assert.Equal(Season, seasonTeam.SeasonId));
        Assert.All(built.Value, seasonTeam => Assert.Equal(0, seasonTeam.FinishTies));
        Assert.Equal(teams.Select(team => (team.Id.Value, team.Division)).Order(), built.Value.Select(seasonTeam => (seasonTeam.TeamId.Value, seasonTeam.Division)).Order());
        Assert.True(SeasonTeam.HasUniqueFinishes(built.Value));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    public void ThePointValueIsTheFinish(int finish, int expected)
    {
        Assert.Equal(expected, SetupTeamFinishes.PointValueFor(finish));
    }

    [Fact]
    public void AMissingTeamIsRejectedNamingTheCount()
    {
        var teams = Catalog();
        var finishes = Valid(teams).Skip(1).ToList();

        var built = SetupTeamFinishes.Create(Season, teams, finishes);

        Assert.Equal("season.teams", built.Error!.Code);
    }

    [Fact]
    public void ADuplicatedTeamIsRejected()
    {
        var teams = Catalog();
        var finishes = Valid(teams);
        finishes[1] = finishes[0];

        Assert.Equal("season.teams", SetupTeamFinishes.Create(Season, teams, finishes).Error!.Code);
    }

    [Fact]
    public void AnUnknownTeamIsRejected()
    {
        var teams = Catalog();
        var finishes = Valid(teams);
        finishes[0] = finishes[0] with { TeamId = new TeamId(Guid.NewGuid()) };

        Assert.Equal("season.teams", SetupTeamFinishes.Create(Season, teams, finishes).Error!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void AFinishOutsideOneToFourIsRejected(int finish)
    {
        var teams = Catalog();
        var finishes = Valid(teams);
        finishes[0] = finishes[0] with { Finish = finish };

        Assert.Equal("season.teams", SetupTeamFinishes.Create(Season, teams, finishes).Error!.Code);
    }

    [Fact]
    public void ADivisionWithTwoTeamsAtTheSameFinishIsRejectedNamingTheDivision()
    {
        var teams = Catalog();
        var finishes = Valid(teams);
        finishes[1] = finishes[1] with { Finish = 1 };

        var built = SetupTeamFinishes.Create(Season, teams, finishes);

        Assert.Equal("season.teams", built.Error!.Code);
        Assert.Contains("AFC East", built.Error.Message);
    }

    [Fact]
    public void NoTeamsAtAllIsRejected()
    {
        Assert.Equal("season.teams", SetupTeamFinishes.Create(Season, Catalog(), []).Error!.Code);
    }
}
