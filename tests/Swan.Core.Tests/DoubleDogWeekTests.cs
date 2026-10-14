using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class DoubleDogWeekTests
{
    private static Season Blank() => Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;

    [Theory]
    [InlineData(new[] { 2, 12, 17 })]
    [InlineData(new[] { 17, 2, 12 })]
    [InlineData(new[] { 2, 3, 12, 17 })]
    [InlineData(new[] { 1, 2, 18 })]
    public void ThreeOrFourUniqueRegularSeasonWeeksIncludingWeekTwoAreValidAndComeBackSorted(int[] weeks)
    {
        var validated = DoubleDogWeekRules.Validate(weeks);

        Assert.True(validated.IsSuccess);
        Assert.Equal(weeks.Order(), validated.Value);
    }

    [Theory]
    [InlineData(new int[] { })]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 2, 12 })]
    [InlineData(new[] { 2, 3, 4, 5, 6 })]
    [InlineData(new[] { 3, 12, 17 })]
    [InlineData(new[] { 2, 12, 12 })]
    [InlineData(new[] { 2, 2, 12 })]
    [InlineData(new[] { 0, 2, 12 })]
    [InlineData(new[] { 2, 12, 19 })]
    [InlineData(new[] { -2, 2, 12 })]
    public void AnythingElseIsRejectedWithAnActionableDoubleDogError(int[] weeks)
    {
        var validated = DoubleDogWeekRules.Validate(weeks);

        Assert.Equal("season.double-dogs", validated.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(validated.Error.Message));
    }

    [Fact]
    public void TheMessagesSayWhatToFix()
    {
        Assert.Contains("Week 2", DoubleDogWeekRules.Validate([3, 12, 17]).Error!.Message);
        Assert.Contains("3 or 4", DoubleDogWeekRules.Validate([2, 12]).Error!.Message);
        Assert.Contains("more than once", DoubleDogWeekRules.Validate([2, 12, 12]).Error!.Message);
        Assert.Contains("1 to 18", DoubleDogWeekRules.Validate([2, 12, 19]).Error!.Message);
    }

    [Fact]
    public void SavingTheSelectionStoresItSortedAndKeepsTheLegacyCountInStep()
    {
        var season = Blank();

        var saved = season.SaveDoubleDogWeeks([17, 2, 12, 5]);

        Assert.True(saved.IsSuccess);
        Assert.Equal([2, 5, 12, 17], season.DoubleDogWeekNumbers);
        Assert.Equal(4, season.Settings.DoubleDogWeeks);
        Assert.Equal((SeasonState.Setup, 2026), (season.State, season.Year));
    }

    [Fact]
    public void AnInvalidSelectionChangesNothing()
    {
        var season = Blank();
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);

        var saved = season.SaveDoubleDogWeeks([3, 4, 5]);

        Assert.Equal("season.double-dogs", saved.Error!.Code);
        Assert.Equal([2, 12, 17], season.DoubleDogWeekNumbers);
        Assert.Equal(3, season.Settings.DoubleDogWeeks);
    }

    [Fact]
    public void ABlankSeasonHasNoSelectionAndCannotActivateUntilOneIsSaved()
    {
        var season = Blank();
        Assert.Empty(season.DoubleDogWeekNumbers);

        Assert.False(season.Activate().IsSuccess);
        Assert.Equal(SeasonState.Setup, season.State);
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        Assert.True(season.Activate().IsSuccess);
    }

    [Fact]
    public void ACountWithoutAPersistedSelectionIsNotEnoughToActivate()
    {
        var buyIn = new Money(100m, "USD");
        var season = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", buyIn)]);

        Assert.False(season.Activate().IsSuccess);
    }

    [Fact]
    public void ALiveSeasonCannotChangeItsSelection()
    {
        var season = Blank();
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        Assert.True(season.Activate().IsSuccess);

        var saved = season.SaveDoubleDogWeeks([2, 3, 4]);

        Assert.Equal("season.locked", saved.Error!.Code);
        Assert.Equal([2, 12, 17], season.DoubleDogWeekNumbers);
    }

    [Fact]
    public void ReplacingSettingsKeepsTheCountDerivedFromTheSelection()
    {
        var season = Blank();
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17, 18]).IsSuccess);

        Assert.True(season.UpdateSettings(season.Settings with { DoubleDogWeeks = 3 }).IsSuccess);

        Assert.Equal(4, season.Settings.DoubleDogWeeks);
    }

    // A schedule week as the holiday rule sees it: its number and the instants its games kick off.
    private static WeekKickoffs Week(int number, params string[] kickoffs) => new(number, kickoffs.Select(DateTimeOffset.Parse).ToList());

    private static readonly WeekKickoffs[] Season2026 =
    [
        Week(11, "2026-11-19T01:15:00Z", "2026-11-22T18:00:00Z", "2026-11-23T01:20:00Z"),
        Week(12, "2026-11-26T17:00:00Z", "2026-11-27T01:20:00Z", "2026-11-29T18:00:00Z"),
        Week(13, "2026-12-04T01:15:00Z", "2026-12-06T18:00:00Z"),
        Week(16, "2026-12-18T01:15:00Z", "2026-12-20T18:00:00Z", "2026-12-21T01:15:00Z"),
        Week(17, "2026-12-25T18:00:00Z", "2026-12-26T21:30:00Z", "2026-12-27T18:00:00Z"),
        Week(18, "2027-01-03T18:00:00Z", "2027-01-03T21:25:00Z"),
    ];

    [Fact]
    public void ThanksgivingIsTheFourthThursdayOfNovemberAndChristmasIsDecember25InEasternTime()
    {
        var holidays = DoubleDogWeekRules.Holidays(2026, Season2026);

        Assert.Equal((new DateOnly(2026, 11, 26), 12), (holidays.Thanksgiving, holidays.ThanksgivingWeek));
        Assert.Equal((new DateOnly(2026, 12, 25), 17), (holidays.Christmas, holidays.ChristmasWeek));
    }

    [Theory]
    [InlineData(2025, 11, 27)]
    [InlineData(2026, 11, 26)]
    [InlineData(2027, 11, 25)]
    [InlineData(2028, 11, 23)]
    public void ThanksgivingFallsOnTheFourthThursday(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), DoubleDogWeekRules.Holidays(year, []).Thanksgiving);
    }

    [Fact]
    public void AGameLateThanksgivingNightInEasternTimeStillBelongsToThatDayEvenThoughItIsUtcFriday()
    {
        var weeks = new[] { Week(11, "2026-11-22T18:00:00Z"), Week(12, "2026-11-27T01:20:00Z"), Week(13, "2026-12-03T01:15:00Z") };

        Assert.Equal(12, DoubleDogWeekRules.Holidays(2026, weeks).ThanksgivingWeek);
    }

    [Fact]
    public void AGameJustBeforeMidnightEasternOnTheDayBeforeDoesNotCountAsTheHoliday()
    {
        var weeks = new[] { Week(11, "2026-11-22T18:00:00Z", "2026-11-26T04:00:00Z"), Week(12, "2026-11-26T17:00:00Z") };

        // 04:00Z on the 26th is 11:00 PM on the 25th in New York, so Week 12 (first game on the 26th) holds Thanksgiving.
        Assert.Equal(12, DoubleDogWeekRules.Holidays(2026, weeks).ThanksgivingWeek);
    }

    [Fact]
    public void ADayWithNoGamesBelongsToTheWeekThatIsRunningThen()
    {
        // No game on Christmas itself or the Tuesday before: Week 17 began on the 20th and Week 18 does not start until the 31st.
        var weeks = new[] { Week(17, "2026-12-20T18:00:00Z", "2026-12-22T01:15:00Z"), Week(18, "2026-12-31T01:15:00Z", "2027-01-03T18:00:00Z") };

        Assert.Equal(17, DoubleDogWeekRules.Holidays(2026, weeks).ChristmasWeek);
    }

    [Fact]
    public void AHolidayBeforeTheFirstWeekOrAfterTheLastWeekHasNoWeek()
    {
        var weeks = new[] { Week(1, "2026-12-26T18:00:00Z"), Week(2, "2026-12-27T18:00:00Z") };

        Assert.Null(DoubleDogWeekRules.Holidays(2026, weeks).ChristmasWeek);
        Assert.Null(DoubleDogWeekRules.Holidays(2026, weeks).ThanksgivingWeek);
        Assert.Null(DoubleDogWeekRules.Holidays(2026, [Week(1, "2026-01-05T18:00:00Z")]).ChristmasWeek);
    }

    [Fact]
    public void BothHolidaysCanLandInTheSameWeekOrOnWeekTwo()
    {
        var oneWeek = new[] { Week(3, "2026-11-26T17:00:00Z", "2026-12-26T18:00:00Z") };
        var holidays = DoubleDogWeekRules.Holidays(2026, oneWeek);

        Assert.Equal((3, 3), (holidays.ThanksgivingWeek, holidays.ChristmasWeek));
        Assert.Equal(2, DoubleDogWeekRules.Holidays(2026, [Week(2, "2026-11-26T17:00:00Z"), Week(3, "2026-12-27T18:00:00Z")]).ThanksgivingWeek);
    }
}
