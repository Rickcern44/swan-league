using Swan.Core.Common;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class ScheduleTests
{
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

    private static Week CreateWeek(out GameId gameId)
    {
        gameId = new GameId(Guid.NewGuid());
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Playoff, "Wild Card", true));
        week.AddGame(new Game(gameId, new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), Kickoff.Create(DateTimeOffset.UtcNow).Value!));
        return week;
    }
}
