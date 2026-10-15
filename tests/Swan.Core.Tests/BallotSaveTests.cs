using Swan.Core.Common;
using Swan.Core.Picks;
using Xunit;

namespace Swan.Core.Tests;

public sealed class BallotSaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Dictionary<string, TeamId> teams = Enumerable.Range(1, 12).ToDictionary(i => $"t{i}", _ => new TeamId(Guid.NewGuid()));
    private readonly Ballot ballot = new(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()));

    private IReadOnlySet<TeamId> Roster => teams.Where(pair => int.Parse(pair.Key[1..]) <= 9).Select(pair => pair.Value).ToHashSet();
    private TeamPick Pick(string team, int lockHours = 24, int points = 2) => new(teams[team], new GameId(Guid.NewGuid()), Now.AddHours(lockHours), PointValue: points);
    private DogPick Dog(string team, int lockHours = 24) => new(teams[team], new GameId(Guid.NewGuid()), Now.AddHours(lockHours));

    [Fact]
    public void ACoachCanSaveFewerThanThreePicksAndALaterSaveReplacesThem()
    {
        var first = Pick("t1");
        var second = Pick("t2");
        Assert.True(ballot.Save([first], [], doubleDog: false, Roster, Now).IsSuccess);

        var saved = ballot.Save([second, Pick("t3")], [Dog("t10")], doubleDog: false, Roster, Now.AddMinutes(5));

        Assert.True(saved.IsSuccess);
        Assert.Equivalent(new[] { teams["t2"], teams["t3"] }, ballot.Picks.Select(pick => pick.TeamId));
        Assert.Single(ballot.Dogs);
        Assert.Single(ballot.Changes);
        Assert.Contains(ballot.DomainEvents, item => item is PickChanged);
    }

    [Fact]
    public void ThePointValueTravelsWithThePick()
    {
        ballot.Save([Pick("t1", points: 4)], [], false, Roster, Now);

        Assert.Equal(4, ballot.Picks.Single().PointValue);
    }

    [Fact]
    public void SavingTheSameSelectionsAgainChangesNothingAndRecordsNothing()
    {
        var picks = new[] { Pick("t1"), Pick("t2") };
        ballot.Save(picks, [], false, Roster, Now);
        var events = ballot.DomainEvents.Count;

        var again = ballot.Save(picks, [], false, Roster, Now.AddMinutes(1));

        Assert.True(again.IsSuccess);
        Assert.Empty(ballot.Changes);
        Assert.Equal(events, ballot.DomainEvents.Count);
    }

    [Fact]
    public void FourPicksATeamTwiceAByeOrAStrangersTeamAreRefusedAndNothingChanges()
    {
        ballot.Save([Pick("t1")], [], false, Roster, Now);

        var tooMany = ballot.Save([Pick("t1"), Pick("t2"), Pick("t3"), Pick("t4")], [], false, Roster, Now);
        var twice = ballot.Save([Pick("t2"), Pick("t2")], [], false, Roster, Now);
        var bye = ballot.Save([Pick("t2") with { IsBye = true }], [], false, Roster, Now);
        var stranger = ballot.Save([Pick("t11")], [], false, Roster, Now);

        Assert.All(new[] { tooMany, twice, bye, stranger }, result => Assert.Equal("pick.invalid", result.Error!.Code));
        Assert.Equal([teams["t1"]], ballot.Picks.Select(pick => pick.TeamId));
    }

    [Fact]
    public void ALockedPickCannotBeRemovedOrReplacedButCanBeKept()
    {
        var early = Pick("t1", lockHours: 1);
        ballot.Save([early, Pick("t2")], [], false, Roster, Now);
        var later = Now.AddHours(2);

        var removed = ballot.Save([Pick("t2")], [], false, Roster, later);
        var replaced = ballot.Save([Pick("t3"), Pick("t2")], [], false, Roster, later);
        var kept = ballot.Save([early, Pick("t5")], [], false, Roster, later);

        Assert.Equal("pick.locked", removed.Error!.Code);
        Assert.Equal("pick.locked", replaced.Error!.Code);
        Assert.True(kept.IsSuccess);
        Assert.Contains(ballot.Picks, pick => pick.TeamId == teams["t1"] && pick.LockTime == early.LockTime);
        Assert.DoesNotContain(ballot.Picks, pick => pick.TeamId == teams["t2"]);
    }

    [Fact]
    public void ANewPickForAGameThatHasAlreadyLockedIsRefused()
    {
        var result = ballot.Save([Pick("t1", lockHours: -1)], [], false, Roster, Now);

        Assert.Equal("pick.locked", result.Error!.Code);
        Assert.Empty(ballot.Picks);
    }

    [Fact]
    public void AnInvalidSaveNeverHalfApplies()
    {
        ballot.Save([Pick("t1"), Pick("t2")], [Dog("t10")], false, Roster, Now);

        var refused = ballot.Save([Pick("t3")], [Dog("t10"), Dog("t10")], false, Roster, Now);

        Assert.False(refused.IsSuccess);
        Assert.Equivalent(new[] { teams["t1"], teams["t2"] }, ballot.Picks.Select(pick => pick.TeamId));
        Assert.Single(ballot.Dogs);
    }

    [Fact]
    public void OneDogIsAllowedAWeekAndTwoDifferentOnesInADoubleDogWeek()
    {
        var a = Dog("t10");
        var b = Dog("t11");

        Assert.Equal("dog.invalid", ballot.Save([], [a, b], doubleDog: false, Roster, Now).Error!.Code);
        Assert.True(ballot.Save([], [a, b], doubleDog: true, Roster, Now).IsSuccess);
        Assert.Equal("dog.invalid", ballot.Save([], [a, b, Dog("t12")], doubleDog: true, Roster, Now).Error!.Code);
        Assert.Equal(2, ballot.Dogs.Count);
    }

    [Fact]
    public void TheSameDogTeamOrGameTwiceInADoubleDogWeekIsRefused()
    {
        var game = new GameId(Guid.NewGuid());
        var sameTeam = new[] { Dog("t10"), Dog("t10") };
        var sameGame = new[] { new DogPick(teams["t10"], game, Now.AddHours(5)), new DogPick(teams["t11"], game, Now.AddHours(5)) };

        Assert.Equal("dog.invalid", ballot.Save([], sameTeam, true, Roster, Now).Error!.Code);
        Assert.Equal("dog.invalid", ballot.Save([], sameGame, true, Roster, Now).Error!.Code);
        Assert.Empty(ballot.Dogs);
    }

    [Fact]
    public void ADogIsNotLimitedToTheRosterAndMayBeAlsoAStartedTeam()
    {
        Assert.True(ballot.Save([Pick("t1")], [Dog("t1"), Dog("t12")], doubleDog: true, Roster, Now).IsSuccess);
    }

    [Fact]
    public void ALockedDogCannotBeRemovedOrChanged()
    {
        var dog = Dog("t10", lockHours: 1);
        ballot.Save([], [dog], false, Roster, Now);
        var later = Now.AddHours(2);

        Assert.Equal("dog.locked", ballot.Save([], [], false, Roster, later).Error!.Code);
        Assert.Equal("dog.locked", ballot.Save([], [Dog("t11")], false, Roster, later).Error!.Code);
        Assert.True(ballot.Save([], [dog], false, Roster, later).IsSuccess);
    }
}
