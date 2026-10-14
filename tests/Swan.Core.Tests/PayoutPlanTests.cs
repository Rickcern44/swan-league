using Swan.Core.Common;
using Swan.Core.League;
using Xunit;

namespace Swan.Core.Tests;

public sealed class PayoutPlanTests
{
    private static Season Blank() => Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;

    private static PayoutPlace P(int position, decimal percent) => new(position, percent);

    private static PayoutPlan Plan(string pool, params PayoutPlace[] places) => new(pool, places);

    private static PayoutPlan[] AllPools(params PayoutPlace[] places) => PayoutRules.Pools.Select(pool => Plan(pool, places)).ToArray();

    private static Money Usd(decimal amount) => new(amount, "USD");

    [Fact]
    public void ABlankSeasonHasAFiveDollarFineAndNoPayoutPlansYet()
    {
        var season = Blank();

        Assert.Equal(Usd(5.00m), season.MissedPickFine);
        Assert.Empty(season.PayoutPlans);
    }

    [Fact]
    public void ALegacySeasonStartsUnconfiguredNotWithInventedChoices()
    {
        var buyIn = Usd(100m);
        var season = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", buyIn)]);

        Assert.Null(season.MissedPickFine);
        Assert.Empty(season.PayoutPlans);
    }

    [Fact]
    public void AValidConfigurationIsStoredInPoolAndPositionOrder()
    {
        var season = Blank();
        PayoutPlan[] plans = [Plan("Playoff", P(1, 100m)), Plan("Main", P(2, 30m), P(1, 70m)), Plan("Dog", P(1, 100m)), Plan("Regular Season", P(1, 60.50m), P(2, 39.50m))];

        var saved = season.SaveFinesAndPayouts(Usd(7.50m), plans);

        Assert.True(saved.IsSuccess);
        Assert.Equal(Usd(7.50m), season.MissedPickFine);
        Assert.Equal(["Main", "Dog", "Regular Season", "Playoff"], season.PayoutPlans.Select(plan => plan.Pool));
        Assert.Equal([(1, 70m), (2, 30m)], season.PayoutPlans[0].Places.Select(place => (place.Position, place.Percent)));
    }

    [Theory]
    [InlineData("Main", new[] { 1 }, new[] { 99.99 })]
    [InlineData("Main", new[] { 1, 2 }, new[] { 70.0, 30.01 })]
    [InlineData("Main", new[] { 1 }, new[] { 100.001 })]
    [InlineData("Main", new[] { 1, 2 }, new[] { 120.0, -20.0 })]
    [InlineData("Main", new[] { 1, 2 }, new[] { 100.0, 0.0 })]
    [InlineData("Main", new[] { 1, 1 }, new[] { 50.0, 50.0 })]
    [InlineData("Main", new[] { 0, 1 }, new[] { 50.0, 50.0 })]
    [InlineData("Main", new[] { -1 }, new[] { 100.0 })]
    public void AnInvalidPoolPlanIsRejectedAndChangesNothing(string pool, int[] positions, double[] percents)
    {
        var season = Blank();
        var before = (season.MissedPickFine, season.PayoutPlans);
        var places = positions.Select((position, index) => P(position, (decimal)percents[index])).ToArray();
        PayoutPlan[] plans = [Plan(pool, places), .. PayoutRules.Pools.Skip(1).Select(other => Plan(other, P(1, 100m)))];

        var saved = season.SaveFinesAndPayouts(Usd(5m), plans);

        Assert.Equal("season.payouts", saved.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(saved.Error.Message));
        Assert.Equal(before, (season.MissedPickFine, season.PayoutPlans));
    }

    [Fact]
    public void ThePlanWithoutPlacesIsRejected()
    {
        var season = Blank();
        PayoutPlan[] plans = [Plan("Main"), .. PayoutRules.Pools.Skip(1).Select(other => Plan(other, P(1, 100m)))];

        Assert.Equal("season.payouts", season.SaveFinesAndPayouts(Usd(5m), plans).Error!.Code);
    }

    [Fact]
    public void ThePoolsMustBeExactlyTheFourCanonicalOnesOnceEach()
    {
        var season = Blank();
        var main = Plan("Main", P(1, 100m));
        PayoutPlan[][] bad =
        [
            [main],
            [main, Plan("Dog", P(1, 100m)), Plan("Regular Season", P(1, 100m))],
            [main, main, Plan("Dog", P(1, 100m)), Plan("Regular Season", P(1, 100m)), Plan("Playoff", P(1, 100m))],
            [main, Plan("Dog", P(1, 100m)), Plan("Regular Season", P(1, 100m)), Plan("Bonus", P(1, 100m))],
            [main, Plan("dog", P(1, 100m)), Plan("Regular Season", P(1, 100m)), Plan("Playoff", P(1, 100m))],
            [],
        ];

        foreach (var plans in bad)
            Assert.Equal("season.payouts", season.SaveFinesAndPayouts(Usd(5m), plans).Error!.Code);
        Assert.Empty(season.PayoutPlans);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5.001)]
    [InlineData(0.001)]
    public void ANonPositiveOrNonCentFineIsRejected(double amount)
    {
        var season = Blank();

        var saved = season.SaveFinesAndPayouts(Usd((decimal)amount), AllPools(P(1, 100m)));

        Assert.Equal("season.payouts", saved.Error!.Code);
        Assert.Equal(Usd(5.00m), season.MissedPickFine);
    }

    [Fact]
    public void AFineInAnotherCurrencyIsRejected()
    {
        Assert.Equal("season.payouts", Blank().SaveFinesAndPayouts(new Money(5m, "EUR"), AllPools(P(1, 100m))).Error!.Code);
    }

    [Fact]
    public void ALiveSeasonCannotChangeItsFinesAndPayouts()
    {
        var season = Blank();
        Assert.True(season.ActivateWithDogs().IsSuccess);

        var saved = season.SaveFinesAndPayouts(Usd(10m), AllPools(P(1, 100m)));

        Assert.Equal("season.locked", saved.Error!.Code);
        Assert.Equal(Usd(5.00m), season.MissedPickFine);
        Assert.Empty(season.PayoutPlans);
    }

    [Theory]
    [InlineData(0, 0, new[] { 100.0 }, new[] { 0.00 })]
    [InlineData(6, 60, new[] { 100.0 }, new[] { 360.00 })]
    [InlineData(6, 60, new[] { 70.0, 30.0 }, new[] { 252.00, 108.00 })]
    [InlineData(3, 20, new[] { 33.33, 33.33, 33.34 }, new[] { 20.01, 19.99, 20.00 })]
    [InlineData(7, 10, new[] { 50.0, 25.0, 25.0 }, new[] { 35.00, 17.50, 17.50 })]
    public void AmountsAreThePercentOfThePotRoundedDownWithTheRemainderGoingToFirstPlace(int coaches, decimal stake, double[] percents, double[] expected)
    {
        var places = percents.Select((percent, index) => P(index + 1, (decimal)percent)).ToArray();

        var amounts = PayoutRules.Split(stake * coaches, places);

        Assert.Equal(expected.Select(value => (decimal)value), amounts);
        Assert.Equal(stake * coaches, amounts.Sum());
    }

    [Fact]
    public void ResidueNeverMakesFirstPlaceNegativeAndTheAmountsAlwaysAddUpToThePot()
    {
        var places = new[] { P(1, 1m), P(2, 33m), P(3, 33m), P(4, 33m) };

        foreach (var pot in new[] { 0.00m, 0.01m, 0.02m, 0.03m, 0.99m, 7.77m, 123.45m })
        {
            var amounts = PayoutRules.Split(pot, places);

            Assert.Equal(pot, amounts.Sum());
            Assert.All(amounts, amount => Assert.True(amount >= 0m));
        }
    }

    [Fact]
    public void TheRemainderGoesToTheLowestPositionEvenWhenItIsNotListedFirstOrNotOne()
    {
        var amounts = PayoutRules.Split(10.00m, [P(3, 33.33m), P(2, 66.67m)]);

        // Returned in position order: 3rd is 3.33 (rounded down) and 2nd, the lowest position, takes the remainder 6.67.
        Assert.Equal([6.67m, 3.33m], amounts);
        Assert.Equal(10.00m, amounts.Sum());
    }
}
