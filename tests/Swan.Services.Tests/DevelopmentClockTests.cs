using Swan.Services.Development;
using Xunit;

namespace Swan.Services.Tests;

public sealed class DevelopmentClockTests
{
    [Fact]
    public void AdvanceMovesOnlyForwardAndNormalizesToUtc()
    {
        var clock = new DevelopmentClock(new DateTimeOffset(2026, 9, 10, 16, 0, 0, TimeSpan.FromHours(-4)));

        Assert.Equal(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero), clock.UtcNow);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 21, 30, 0, TimeSpan.Zero), clock.Advance(TimeSpan.FromMinutes(90)));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromMinutes(-1)));
    }
}
