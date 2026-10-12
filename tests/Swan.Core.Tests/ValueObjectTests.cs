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
