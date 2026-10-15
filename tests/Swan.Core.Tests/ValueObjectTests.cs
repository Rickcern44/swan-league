using Swan.Core.Common;
using Xunit;

namespace Swan.Core.Tests;

public sealed class ValueObjectTests
{
    [Fact]
    public void MoneyRejectsNegativeAmounts() => Assert.False(Money.Create(-1, "USD").IsSuccess);

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void PointValueIsLimitedToOneThroughFour(int value) => Assert.False(PointValue.Create(value).IsSuccess);

    [Fact]
    public void DogPointsRoundsUp() => Assert.Equal(7, DogPoints.Create(6.5m).Value!.Rounded);

    [Fact]
    public void GameResultDerivesWinnerAndTie()
    {
        Assert.Equal(GameWinner.Home, GameResult.Create(21, 14).Value!.Winner);
        Assert.Equal(GameWinner.Tie, GameResult.Create(21, 21).Value!.Winner);
    }

    [Theory]
    [InlineData(-30.5)]
    [InlineData(30.5)]
    [InlineData(35)]
    [InlineData(-350)]
    [InlineData(1000)]
    public void SpreadBeyondThirtyPointsEitherWayIsRejected(double value)
    {
        var result = Spread.Create((decimal)value);

        Assert.False(result.IsSuccess);
        Assert.Equal("spread.invalid", result.Error!.Code);
        Assert.Equal("A spread must be between -30 and 30.", result.Error.Message);
    }

    [Fact]
    public void TheLargestAndSmallestDecimalsAreRejectedNotAnOverflow()
    {
        foreach (var value in new[] { decimal.MinValue, decimal.MaxValue })
        {
            var result = Spread.Create(value);

            Assert.False(result.IsSuccess);
            Assert.Equal("spread.invalid", result.Error!.Code);
        }
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(-3.5)]
    [InlineData(30)]
    public void SpreadsFromMinusThirtyToThirtyAreAccepted(double value) => Assert.True(Spread.Create((decimal)value).IsSuccess);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(101, 0)]
    [InlineData(0, 101)]
    [InlineData(999, 3)]
    public void ScoresMustBeWholeNumbersFromZeroToOneHundred(int home, int away)
    {
        var result = GameResult.Create(home, away);

        Assert.False(result.IsSuccess);
        Assert.Equal("game-result.invalid", result.Error!.Code);
        Assert.Equal("Scores must be between 0 and 100.", result.Error.Message);
    }

    [Fact]
    public void ScoresOfZeroAndOneHundredAreAccepted() => Assert.True(GameResult.Create(0, 100).IsSuccess);

    [Fact]
    public void EmailRejectsMalformedAddresses() => Assert.False(Email.Create("not-an-email").IsSuccess);

    [Fact]
    public void AggregateCollectsEvents()
    {
        var aggregate = new TestAggregate();
        aggregate.Record(new TestEvent());
        Assert.Single(aggregate.DomainEvents);
    }

    private sealed class TestAggregate : AggregateRoot
    {
        public void Record(IDomainEvent domainEvent) => Raise(domainEvent);
    }

    private sealed record TestEvent : IDomainEvent;
}
