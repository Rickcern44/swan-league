using Swan.Core.Common;
using Xunit;

namespace Swan.Core.Tests;

public sealed class EasternTimeTests
{
    [Theory]
    [InlineData(2026, 10, 20, 18, 0, "2026-10-20T22:00:00+00:00")] // EDT, UTC-4
    [InlineData(2026, 12, 20, 18, 0, "2026-12-20T23:00:00+00:00")] // EST, UTC-5
    [InlineData(2026, 11, 1, 2, 30, "2026-11-01T07:30:00+00:00")] // 2:30 AM after the fall-back hour is plain EST
    public void AnEasternWallClockTimeBecomesTheMatchingInstant(int y, int mo, int d, int h, int mi, string expected)
    {
        var result = EasternTime.ToInstant(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Unspecified));

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse(expected), result.Value);
    }

    [Fact]
    public void ATimeThatDoesNotExistBecauseTheClocksSkipItIsRefused()
    {
        var result = EasternTime.ToInstant(new DateTime(2027, 3, 14, 2, 30, 0, DateTimeKind.Unspecified));

        Assert.Equal("season.draft-deadline", result.Error!.Code);
        Assert.Contains("does not exist", result.Error.Message);
    }

    [Fact]
    public void ATimeThatHappensTwiceBecauseTheClocksRepeatItIsRefused()
    {
        var result = EasternTime.ToInstant(new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified));

        Assert.Equal("season.draft-deadline", result.Error!.Code);
        Assert.Contains("twice", result.Error.Message);
    }

    [Fact]
    public void TheInstantIsShownBackAsEasternTime()
    {
        var instant = DateTimeOffset.Parse("2026-10-20T22:00:00+00:00");

        Assert.Equal("2026-10-20T18:00", EasternTime.ToLocalText(instant));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1899)]
    [InlineData(2201)]
    [InlineData(9999)]
    public void AYearOutsideAnyRealSeasonIsRefusedInsteadOfCrashing(int year)
    {
        var result = EasternTime.ToInstant(new DateTime(year, 12, 31, 23, 59, 0, DateTimeKind.Unspecified));

        Assert.Equal("season.draft-deadline", result.Error!.Code);
        Assert.Contains("year", result.Error.Message);
    }
}
