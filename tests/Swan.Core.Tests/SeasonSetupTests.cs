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
}
