using Swan.Core.Communication;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class InvariantCoverageTests
{
    [Fact]
    public void RosterRequiresNineDistinctTeamsToClose()
    {
        var roster = NewRoster();
        Assert.False(roster.Close().IsSuccess);
        foreach (var team in TeamIds(9)) Assert.True(roster.AddTeam(team, true).IsSuccess);
        Assert.False(roster.AddTeam(roster.Teams.First(), true).IsSuccess);
        Assert.True(roster.Close().IsSuccess);
    }

    [Fact]
    public void RosterChangesOnlyWhileWindowIsOpen()
    {
        var roster = NewRoster();
        Assert.False(roster.AddTeam(NewTeamId(), false).IsSuccess);
        Assert.True(roster.AddTeam(NewTeamId(), true).IsSuccess);
        Assert.False(roster.Correct(roster.Teams.Single(), NewTeamId(), false, "late").IsSuccess);
    }

    [Fact]
    public void RosterCorrectionRequiresReason()
    {
        var roster = NewRoster();
        roster.AddTeam(NewTeamId(), true);
        Assert.False(roster.Correct(roster.Teams.Single(), NewTeamId(), true, " ").IsSuccess);
    }

    [Fact]
    public void BallotLimitsPicksToThreeDistinctRosterTeams()
    {
        var teams = TeamIds(4);
        var ballot = NewBallot(teams);
        foreach (var team in teams.Take(3)) Assert.True(ballot.AddPick(Pick(team), DateTimeOffset.UtcNow).IsSuccess);
        Assert.False(ballot.AddPick(Pick(teams[3]), DateTimeOffset.UtcNow).IsSuccess);
    }

    [Fact]
    public void BallotRejectsTeamsOutsideRoster()
    {
        var ballot = NewBallot(TeamIds(1));
        Assert.False(ballot.AddPick(Pick(NewTeamId()), DateTimeOffset.UtcNow).IsSuccess);
    }

    [Fact]
    public void BallotRejectsByeTeams()
    {
        var team = NewTeamId();
        Assert.False(NewBallot([team]).AddPick(Pick(team) with { IsBye = true }, DateTimeOffset.UtcNow).IsSuccess);
    }

    [Fact]
    public void BallotRejectsChangesAtPickLockTime()
    {
        var teams = TeamIds(2);
        var now = DateTimeOffset.UtcNow;
        var ballot = NewBallot(teams);
        var original = Pick(teams[0], now.AddMinutes(1));
        ballot.AddPick(original, now);
        Assert.False(ballot.ChangePick(teams[0], Pick(teams[1], now.AddHours(1)), now.AddMinutes(1), "late").IsSuccess);
    }

    [Fact]
    public void BallotAllowsOneOrTwoDogsBasedOnWeekFlag()
    {
        var ballot = NewBallot(TeamIds(3));
        var now = DateTimeOffset.UtcNow;
        Assert.True(ballot.AddDog(Dog(ballot.Picks.Count == 0 ? NewTeamId() : NewTeamId()), false, now).IsSuccess);
        Assert.False(ballot.AddDog(Dog(NewTeamId()), false, now).IsSuccess);
        var doubleDog = NewBallot(TeamIds(3));
        Assert.True(doubleDog.AddDog(Dog(NewTeamId()), true, now).IsSuccess);
        Assert.True(doubleDog.AddDog(Dog(NewTeamId()), true, now).IsSuccess);
        Assert.False(doubleDog.AddDog(Dog(NewTeamId()), true, now).IsSuccess);
    }

    [Fact]
    public void BallotDogsRequireDistinctTeamsAndGames()
    {
        var now = DateTimeOffset.UtcNow;
        var ballot = NewBallot(TeamIds(2));
        var team = NewTeamId();
        var game = new GameId(Guid.NewGuid());
        Assert.True(ballot.AddDog(new DogPick(team, game, now.AddHours(1)), true, now).IsSuccess);
        Assert.False(ballot.AddDog(new DogPick(team, new GameId(Guid.NewGuid()), now.AddHours(1)), true, now).IsSuccess);
        Assert.False(ballot.AddDog(new DogPick(NewTeamId(), game, now.AddHours(1)), true, now).IsSuccess);
    }

    [Fact]
    public void BallotAppendsEachPickChangeToHistory()
    {
        var teams = TeamIds(2);
        var now = DateTimeOffset.UtcNow;
        var ballot = NewBallot(teams);
        ballot.AddPick(Pick(teams[0]), now);
        Assert.True(ballot.ChangePick(teams[0], Pick(teams[1]), now, "injury").IsSuccess);
        Assert.Single(ballot.Changes);
    }

    [Fact]
    public void BallotIsCompleteAfterThreePicks()
    {
        var teams = TeamIds(3);
        var ballot = NewBallot(teams);
        foreach (var team in teams) ballot.AddPick(Pick(team), DateTimeOffset.UtcNow);
        Assert.True(ballot.IsComplete);
    }

    [Fact]
    public void SeasonRequiresPoolStakesToEqualBuyIn()
    {
        var buyIn = Money.Create(100m, "USD").Value!;
        var season = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", Money.Create(99m, "USD").Value!)]);
        Assert.False(season.Activate().IsSuccess);
    }

    [Fact]
    public void SeasonRequiresAtLeastThreeDoubleDogWeeks()
    {
        var buyIn = Money.Create(100m, "USD").Value!;
        var season = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 2), [new PoolDefinition("Main", buyIn)]);
        Assert.False(season.Activate().IsSuccess);
    }

    [Fact]
    public void SeasonStructuralSettingsAreLockedOutsideSetup()
    {
        var buyIn = Money.Create(100m, "USD").Value!;
        var season = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", buyIn)]);
        season.ActivateWithDogs();
        Assert.False(season.UpdateSettings(new SeasonSettings(buyIn, 4)).IsSuccess);
    }

    [Fact]
    public void MembershipDeactivationPreservesTheMembershipRecord()
    {
        var membership = new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), MembershipRole.Coach);
        membership.Deactivate();
        Assert.False(membership.IsActive);
        Assert.NotEqual(default, membership.Id);
    }

    [Fact]
    public void TeamRequiresADivision()
    {
        Assert.False(Team.Create(NewTeamId(), "Swans", null, DateTimeOffset.UtcNow, "").IsSuccess);
        Assert.True(Team.Create(NewTeamId(), "Swans", null, DateTimeOffset.UtcNow, "East").IsSuccess);
    }

    [Fact]
    public void AnnouncementRequiresAFutureSchedule()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(Announcement.Schedule(new AnnouncementId(Guid.NewGuid()), "News", now, now).IsSuccess);
    }

    [Fact]
    public void AnnouncementSendsIdempotently()
    {
        var now = DateTimeOffset.UtcNow;
        var announcement = Announcement.Schedule(new AnnouncementId(Guid.NewGuid()), "News", now.AddMinutes(1), now).Value!;
        Assert.True(announcement.Send(now.AddMinutes(1)).IsSuccess);
        Assert.True(announcement.Send(now.AddMinutes(2)).IsSuccess);
        Assert.True(announcement.IsSent);
    }

    [Fact]
    public void EssentialNotificationCategoriesCannotBeMuted()
    {
        Assert.False(new NotificationPreferences(new MembershipId(Guid.NewGuid())).Mute(NotificationCategory.Invitations).IsSuccess);
    }

    private static Roster NewRoster() => new(new RosterId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()));
    private static Ballot NewBallot(IEnumerable<TeamId> teams) => new(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()), teams);
    private static TeamPick Pick(TeamId team, DateTimeOffset? lockTime = null) => new(team, new GameId(Guid.NewGuid()), lockTime ?? DateTimeOffset.UtcNow.AddHours(1));
    private static DogPick Dog(TeamId team) => new(team, new GameId(Guid.NewGuid()), DateTimeOffset.UtcNow.AddHours(1));
    private static TeamId NewTeamId() => new(Guid.NewGuid());
    private static List<TeamId> TeamIds(int count) => Enumerable.Range(0, count).Select(_ => NewTeamId()).ToList();
}
