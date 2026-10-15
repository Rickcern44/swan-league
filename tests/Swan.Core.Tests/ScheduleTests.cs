using Swan.Core.Common;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class ScheduleTests
{
    [Fact]
    public void LinesCannotBeApprovedWhileANewerLineIsStillAwaitingApproval()
    {
        var week = CreateWeek(out var gameId);
        week.AddLine(gameId, Spread.Create(-3.5m).Value!);
        week.ApproveLatestLine(gameId);
        week.AddLine(gameId, Spread.Create(-4.5m).Value!);

        var refused = week.ApproveLines();

        Assert.False(refused.IsSuccess);
        Assert.Equal("week.lines", refused.Error!.Code);
        Assert.Equal(WeekState.Draft, week.State);
        week.ApproveLatestLine(gameId);
        Assert.True(week.ApproveLines().IsSuccess);
        Assert.Equal(-4.5m, week.Games.Single().ApprovedLine!.Value);
    }

    [Fact]
    public void WeekFollowsLifecycleAndRaisesEvents()
    {
        var week = CreateWeek(out var gameId);
        Assert.True(week.AddLine(gameId, Spread.Create(-3.5m).Value!).IsSuccess);
        Assert.True(week.ApproveLatestLine(gameId).IsSuccess);
        Assert.True(week.ApproveLines().IsSuccess);
        Assert.True(week.Publish().IsSuccess);
        Assert.True(week.Lock().IsSuccess);
        Assert.True(week.ConfirmResult(gameId, GameResult.Create(21, 14).Value!).IsSuccess);
        Assert.True(week.FinalizeResults().IsSuccess);
        Assert.True(week.Reopen().IsSuccess);
        Assert.Contains(week.DomainEvents, domainEvent => domainEvent is WeekPublished);
        Assert.Contains(week.DomainEvents, domainEvent => domainEvent is WeekLocked);
        Assert.Contains(week.DomainEvents, domainEvent => domainEvent is ResultsConfirmed);
    }

    [Fact]
    public void PublishRequiresApprovedLinesAndLinesFreezeAfterPublish()
    {
        var week = CreateWeek(out var gameId);
        Assert.False(week.ApproveLines().IsSuccess);
        week.AddLine(gameId, Spread.Create(2.5m).Value!);
        week.ApproveLatestLine(gameId);
        week.ApproveLines();
        week.Publish();
        Assert.False(week.AddLine(gameId, Spread.Create(3m).Value!).IsSuccess);
    }

    [Fact]
    public void DivisionFinishMustBeUnique()
    {
        var season = new SeasonId(Guid.NewGuid());
        var teams = new[] { new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), season, new TeamId(Guid.NewGuid()), "East", 1, 0), new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), season, new TeamId(Guid.NewGuid()), "East", 1, 0) };
        Assert.False(SeasonTeam.HasUniqueFinishes(teams));
    }

    [Fact]
    public void ResultsCanOnlyBeConfirmedWhileWeekIsLocked()
    {
        var week = CreateWeek(out var gameId);
        var result = GameResult.Create(21, 14).Value!;
        Assert.Equal("week.state", week.ConfirmResult(gameId, result).Error!.Code);
        week.AddLine(gameId, Spread.Create(-3m).Value!);
        week.ApproveLatestLine(gameId);
        week.ApproveLines();
        week.Publish();
        Assert.False(week.ConfirmResult(gameId, result).IsSuccess);
        week.Lock();
        Assert.True(week.ConfirmResult(gameId, result).IsSuccess);
    }

    [Fact]
    public void GeneratedDogOptionsFollowTheHomePerspectiveSpread()
    {
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        var (homeDog, awayDog, pickem, excluded) = (AddGame(week, 3m), AddGame(week, -3m), AddGame(week, 0m), AddGame(week, 7m, excluded: true));

        Assert.True(week.GenerateDogOptions().IsSuccess);

        Assert.Equal(Ids(homeDog.Home, awayDog.Away), Ids(week.DogOptions.Select(option => option.TeamId)));
        Assert.DoesNotContain(week.DogOptions, option => option.TeamId == pickem.Home || option.TeamId == pickem.Away || option.TeamId == excluded.Home || option.TeamId == excluded.Away);
    }

    [Fact]
    public void DogOptionsAreNotGeneratedFromPartialLinesOrOutsideDraft()
    {
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        var approved = AddGame(week, 3m);
        Assert.True(week.ReplaceDogOptions([approved.Away]).IsSuccess);
        var unapproved = new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow));
        week.AddGame(unapproved);
        week.AddLine(unapproved.Id, new Spread(2m));

        Assert.Equal("week.lines", week.GenerateDogOptions().Error!.Code);
        Assert.Equal([approved.Away], week.DogOptions.Select(option => option.TeamId));
        week.ApproveLatestLine(unapproved.Id);
        week.ApproveLines();
        week.Publish();
        Assert.Equal("week.state", week.GenerateDogOptions().Error!.Code);
    }

    [Fact]
    public void ReplacingDogOptionsAllowsEitherSideOfAnIncludedGameButNothingElse()
    {
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        var first = AddGame(week, 3m);
        var second = AddGame(week, -3m);
        var excluded = AddGame(week, 1m, excluded: true);
        Assert.True(week.GenerateDogOptions().IsSuccess);

        Assert.Equal("week.dogs", week.ReplaceDogOptions([first.Home, first.Home]).Error!.Code);
        Assert.Equal("week.dogs", week.ReplaceDogOptions([new TeamId(Guid.NewGuid())]).Error!.Code);
        Assert.Equal("week.dogs", week.ReplaceDogOptions([excluded.Home]).Error!.Code);
        Assert.Equal(Ids(first.Home, second.Away), Ids(week.DogOptions.Select(option => option.TeamId)));

        Assert.True(week.ReplaceDogOptions([first.Away, second.Home]).IsSuccess);
        Assert.Equal(Ids(first.Away, second.Home), Ids(week.DogOptions.Select(option => option.TeamId)));
        week.ApproveLines();
        week.Publish();
        Assert.Equal("week.state", week.ReplaceDogOptions([first.Home]).Error!.Code);
    }

    [Fact]
    public void ApprovingAndPublishingDoNotRequireADogList()
    {
        var week = CreateWeek(out var gameId);
        week.AddLine(gameId, new Spread(1m));
        week.ApproveLatestLine(gameId);
        Assert.True(week.ApproveLines().IsSuccess);
        Assert.Empty(week.DogOptions);
        Assert.True(week.Publish().IsSuccess);
    }

    private static IEnumerable<Guid> Ids(params TeamId[] teams) => Ids(teams.AsEnumerable());
    private static IEnumerable<Guid> Ids(IEnumerable<TeamId> teams) => teams.Select(team => team.Value).Order().ToList();

    private static (TeamId Home, TeamId Away) AddGame(Week week, decimal spread, bool excluded = false)
    {
        var game = new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow), excluded);
        week.AddGame(game);
        week.AddLine(game.Id, new Spread(spread));
        week.ApproveLatestLine(game.Id);
        return (game.HomeTeamId, game.AwayTeamId);
    }

    private static Week CreateWeek(out GameId gameId)
    {
        gameId = new GameId(Guid.NewGuid());
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Playoff, "Wild Card", true));
        week.AddGame(new Game(gameId, new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), Kickoff.Create(DateTimeOffset.UtcNow).Value!));
        return week;
    }
}
