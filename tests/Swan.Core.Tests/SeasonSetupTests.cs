using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Xunit;

namespace Swan.Core.Tests;

public sealed class SeasonSetupTests
{
    [Fact]
    public void BlankSeasonStartsInSetupWithPrdDefaultsAndCanBeActivated()
    {
        var created = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "  2026 Season  ", isSimulation: true);

        Assert.True(created.IsSuccess);
        var season = created.Value!;
        Assert.Equal((2026, "2026 Season", true, SeasonState.Setup), (season.Year, season.Name, season.IsSimulation, season.State));
        Assert.Equal(new Money(100m, "USD"), season.Settings.BuyIn);
        Assert.Equal(3, season.Settings.DoubleDogWeeks);
        Assert.Equal(new TimeOnly(13, 0), season.Settings.TeamPickDeadlineEastern);
        Assert.Equal([("Main", 60m), ("Dog", 20m), ("Regular Season", 10m), ("Playoff", 10m)], season.Pools.Select(pool => (pool.Name, pool.Stake.Amount)));
        Assert.All(season.Pools, pool => Assert.Equal("USD", pool.Stake.Currency));
        Assert.True(season.Activate().IsSuccess);
    }

    [Theory]
    [InlineData(null, 2026, "season.name")]
    [InlineData("", 2026, "season.name")]
    [InlineData("   ", 2026, "season.name")]
    [InlineData("2026", 1999, "season.year")]
    [InlineData("2026", 2101, "season.year")]
    public void InvalidIdentityIsRejectedWithoutThrowing(string? name, int year, string code)
    {
        var created = Season.CreateBlank(new SeasonId(Guid.NewGuid()), year, name!, isSimulation: false);

        Assert.Equal(code, created.Error!.Code);
    }

    [Fact]
    public void SetupSeasonBasicsCanBeRevisedWithoutTouchingPoolsOrSettings()
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;
        var (settings, pools) = (season.Settings, season.Pools);

        var updated = season.UpdateBasics(2027, "  2027 Season  ", isSimulation: true);

        Assert.True(updated.IsSuccess);
        Assert.Equal((2027, "2027 Season", true, SeasonState.Setup), (season.Year, season.Name, season.IsSimulation, season.State));
        Assert.Equal(settings, season.Settings);
        Assert.Equal(pools, season.Pools);
    }

    [Theory]
    [InlineData("", 2027, "season.name")]
    [InlineData("   ", 2027, "season.name")]
    [InlineData("2027", 1999, "season.year")]
    [InlineData("2027", 2101, "season.year")]
    public void InvalidBasicsAreRejectedAndLeaveTheSeasonUnchanged(string name, int year, string code)
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;

        var updated = season.UpdateBasics(year, name, isSimulation: true);

        Assert.Equal(code, updated.Error!.Code);
        Assert.Equal((2026, "2026 Season", false), (season.Year, season.Name, season.IsSimulation));
    }

    [Fact]
    public void LiveSeasonRejectsAnyBasicsUpdate()
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;
        Assert.True(season.Activate().IsSuccess);

        var updated = season.UpdateBasics(2027, "2027 Season", isSimulation: true);

        Assert.Equal("season.locked", updated.Error!.Code);
        Assert.Equal((2026, "2026 Season", false), (season.Year, season.Name, season.IsSimulation));
    }

    private static Money Usd(decimal amount) => new(amount, "USD");

    private static Season Blank() => Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;

    [Fact]
    public void SetupMoneyCanBeRevisedAndStillActivates()
    {
        var season = Blank();
        var deadline = season.Settings.TeamPickDeadlineEastern;

        var updated = season.UpdateMoney(Usd(150m), Usd(90m), Usd(30m), Usd(15m), Usd(15m));

        Assert.True(updated.IsSuccess);
        Assert.Equal(Usd(150m), season.Settings.BuyIn);
        Assert.Equal([("Main", 90m), ("Dog", 30m), ("Regular Season", 15m), ("Playoff", 15m)], season.Pools.Select(pool => (pool.Name, pool.Stake.Amount)));
        Assert.Equal((3, deadline, SeasonState.Setup), (season.Settings.DoubleDogWeeks, season.Settings.TeamPickDeadlineEastern, season.State));
        Assert.True(season.Activate().IsSuccess);
    }

    [Theory]
    [InlineData(100, 60, 20, 10, 5)]
    [InlineData(100, 60, 20, 10, 15)]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(-100, 60, 20, 10, 10)]
    [InlineData(100, 70, 20, 10, 0)]
    [InlineData(100, 80, 30, -10, 0)]
    [InlineData(0.004, 0.001, 0.001, 0.001, 0.001)]
    public void InvalidMoneyIsRejectedAndLeavesTheSeasonUnchanged(decimal buyIn, decimal main, decimal dog, decimal regular, decimal playoff)
    {
        var season = Blank();
        var (settings, pools) = (season.Settings, season.Pools);

        var updated = season.UpdateMoney(Usd(buyIn), Usd(main), Usd(dog), Usd(regular), Usd(playoff));

        Assert.Equal("season.money", updated.Error!.Code);
        Assert.Equal(settings, season.Settings);
        Assert.Equal(pools, season.Pools);
    }

    [Fact]
    public void AmountsThatWouldOverflowDecimalAreRejectedNotThrown()
    {
        var season = Blank();
        var max = Usd(decimal.MaxValue);

        var updated = season.UpdateMoney(max, max, max, max, max);
        var mismatched = season.UpdateMoney(Usd(100m), max, max, max, max);

        Assert.Equal("season.money", updated.Error!.Code);
        Assert.Equal("season.money", mismatched.Error!.Code);
    }

    [Fact]
    public void NonUsdMoneyIsRejected()
    {
        var season = Blank();

        var updated = season.UpdateMoney(new Money(100m, "EUR"), new Money(60m, "EUR"), new Money(20m, "EUR"), new Money(10m, "EUR"), new Money(10m, "EUR"));

        Assert.Equal("season.money", updated.Error!.Code);
        Assert.Equal(Usd(100m), season.Settings.BuyIn);
    }

    [Fact]
    public void LiveSeasonRejectsAnyMoneyUpdate()
    {
        var season = Blank();
        Assert.True(season.Activate().IsSuccess);

        var updated = season.UpdateMoney(Usd(150m), Usd(90m), Usd(30m), Usd(15m), Usd(15m));

        Assert.Equal("season.locked", updated.Error!.Code);
        Assert.Equal(Usd(100m), season.Settings.BuyIn);
        Assert.Equal(60m, season.Pools.Single(pool => pool.Name == "Main").Stake.Amount);
    }

    [Fact]
    public void SetupDeadlineCanBeRevisedAndOnlyTheDeadlineChanges()
    {
        var season = Blank();
        var (settings, pools, id) = (season.Settings, season.Pools, season.Id);

        var updated = season.UpdateDeadline(new TimeOnly(11, 30));

        Assert.True(updated.IsSuccess);
        Assert.Equal(settings with { TeamPickDeadlineEastern = new TimeOnly(11, 30) }, season.Settings);
        Assert.Equal(pools, season.Pools);
        Assert.Equal((id, 2026, "2026 Season", false, SeasonState.Setup), (season.Id, season.Year, season.Name, season.IsSimulation, season.State));
        Assert.True(season.Activate().IsSuccess);
    }

    [Fact]
    public void LiveSeasonRejectsADeadlineUpdate()
    {
        var season = Blank();
        Assert.True(season.Activate().IsSuccess);

        var updated = season.UpdateDeadline(new TimeOnly(11, 30));

        Assert.Equal("season.locked", updated.Error!.Code);
        Assert.Equal(new TimeOnly(13, 0), season.Settings.TeamPickDeadlineEastern);
    }

    [Fact]
    public void ConfiguredDeadlineChangesTheLockPolicyDeadline()
    {
        var season = Blank();
        var weekStart = new DateTimeOffset(2025, 10, 6, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2025, 10, 12, 17, 0, 0, TimeSpan.Zero), LockPolicy.WeekDeadline(season.Settings, weekStart));

        Assert.True(season.UpdateDeadline(new TimeOnly(11, 30)).IsSuccess);

        Assert.Equal(new DateTimeOffset(2025, 10, 12, 15, 30, 0, TimeSpan.Zero), LockPolicy.WeekDeadline(season.Settings, weekStart));
        var lateSunday = new DateTimeOffset(2025, 10, 12, 20, 25, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2025, 10, 12, 15, 30, 0, TimeSpan.Zero), LockPolicy.TeamPickLockTime(lateSunday, season.Settings, weekStart));
    }

    [Fact]
    public void ABlankSeasonResumesAtBasics()
    {
        Assert.Equal(SetupStep.Basics, Blank().SetupStep);
    }

    [Theory]
    [InlineData(SetupStep.Money)]
    [InlineData(SetupStep.WeeklyDeadline)]
    [InlineData(SetupStep.TeamsAndPoints)]
    [InlineData(SetupStep.ScheduleImport)]
    [InlineData(SetupStep.WeeksAndDogs)]
    [InlineData(SetupStep.Basics)]
    public void SetupResumeStepCanBeSavedAndOnlyItChanges(SetupStep step)
    {
        var season = Blank();
        var (settings, pools, year, name) = (season.Settings, season.Pools, season.Year, season.Name);

        var saved = season.SaveSetupStep(step);

        Assert.True(saved.IsSuccess);
        Assert.Equal(step, season.SetupStep);
        Assert.Equal((settings, pools, year, name, SeasonState.Setup), (season.Settings, season.Pools, season.Year, season.Name, season.State));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public void UnknownSetupStepIsRejectedAndLeavesTheStep(int value)
    {
        var season = Blank();
        Assert.True(season.SaveSetupStep(SetupStep.Money).IsSuccess);

        var saved = season.SaveSetupStep((SetupStep)value);

        Assert.Equal("season.step", saved.Error!.Code);
        Assert.Equal(SetupStep.Money, season.SetupStep);
    }

    [Fact]
    public void LiveSeasonRejectsASetupStepUpdate()
    {
        var season = Blank();
        Assert.True(season.Activate().IsSuccess);

        var saved = season.SaveSetupStep(SetupStep.Money);

        Assert.Equal("season.locked", saved.Error!.Code);
        Assert.Equal(SetupStep.Basics, season.SetupStep);
    }
}
