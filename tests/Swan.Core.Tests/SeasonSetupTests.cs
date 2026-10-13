using Swan.Core.Common;
using Swan.Core.League;
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
}
