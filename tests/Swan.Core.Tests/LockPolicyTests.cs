using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Xunit;

namespace Swan.Core.Tests;

public sealed class LockPolicyTests
{
    private static readonly SeasonSettings Settings = new(new Money(100m, "USD"), 3);

    [Theory]
    [InlineData("2025-10-09T20:15:00Z")]
    [InlineData("2025-10-10T20:15:00Z")]
    [InlineData("2025-10-11T20:15:00Z")]
    [InlineData("2025-10-12T13:30:00Z")]
    public void TeamPicksLockAtThursdayFridaySaturdayAndLondonKickoffs(string kickoffValue)
    {
        var kickoff = DateTimeOffset.Parse(kickoffValue);

        var lockTime = LockPolicy.TeamPickLockTime(kickoff, Settings, new DateTimeOffset(2025, 10, 6, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(kickoff, lockTime);
    }

    [Fact]
    public void NormalSundayTeamPicksLockAtTheEarlierOfKickoffAndOnePmEasternDeadline()
    {
        var weekStart = new DateTimeOffset(2025, 10, 6, 0, 0, 0, TimeSpan.Zero);
        var earlySunday = new DateTimeOffset(2025, 10, 12, 16, 0, 0, TimeSpan.Zero);
        var lateSunday = new DateTimeOffset(2025, 10, 12, 20, 25, 0, TimeSpan.Zero);

        Assert.Equal(earlySunday, LockPolicy.TeamPickLockTime(earlySunday, Settings, weekStart));
        Assert.Equal(new DateTimeOffset(2025, 10, 12, 17, 0, 0, TimeSpan.Zero), LockPolicy.TeamPickLockTime(lateSunday, Settings, weekStart));
    }

    [Fact]
    public void MondayDogLocksAtItsOwnKickoff()
    {
        var kickoff = new DateTimeOffset(2025, 10, 14, 0, 15, 0, TimeSpan.Zero);

        Assert.Equal(kickoff, LockPolicy.DogPickLockTime(kickoff));
    }

    [Theory]
    [InlineData("2025-03-03T00:00:00Z", "2025-03-09T17:00:00Z")]
    [InlineData("2025-10-27T00:00:00Z", "2025-11-02T18:00:00Z")]
    public void DeadlineUsesCorrectEasternOffsetAcrossDst(string weekStartValue, string expectedValue)
    {
        var deadline = LockPolicy.WeekDeadline(Settings, DateTimeOffset.Parse(weekStartValue));

        Assert.Equal(DateTimeOffset.Parse(expectedValue), deadline);
    }

    [Fact]
    public void BallotFactoryUsesPolicyLockAndCannotChangeAtLockTime()
    {
        var now = new DateTimeOffset(2025, 10, 9, 19, 0, 0, TimeSpan.Zero);
        var team = new TeamId(Guid.NewGuid());
        var game = new GameId(Guid.NewGuid());
        var pick = TeamPick.ForGame(team, game, new DateTimeOffset(2025, 10, 9, 20, 15, 0, TimeSpan.Zero), Settings, new DateTimeOffset(2025, 10, 6, 0, 0, 0, TimeSpan.Zero));
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()), [team]);

        Assert.True(ballot.AddPick(pick, now).IsSuccess);
        Assert.False(ballot.ChangePick(team, pick, pick.LockTime, "late").IsSuccess);
    }
}
