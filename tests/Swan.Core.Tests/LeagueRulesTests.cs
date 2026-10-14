using Swan.Core.Common;
using Swan.Core.League;
using Xunit;

namespace Swan.Core.Tests;

public sealed class LeagueRulesTests
{
    private static Season Blank() => Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;

    private static PointColor[] Colors(params (int Points, ColorTier Color)[] entries) => entries.Select(entry => new PointColor(entry.Points, entry.Color)).ToArray();

    [Fact]
    public void TheDefaultsAreRuleSeventeenOnGreenBlueYellowRedAndFirstOccurrence()
    {
        var rules = LeagueRules.Default;

        Assert.True(rules.TeamMayAlsoBeDog);
        Assert.Equal(RecordHolderMode.FirstOccurrence, rules.RecordHolder);
        Assert.Equal([(1, ColorTier.Green), (2, ColorTier.Blue), (3, ColorTier.Yellow), (4, ColorTier.Red)], rules.PointColors.Entries.Select(entry => (entry.Points, entry.Color)));
    }

    [Fact]
    public void ABlankSeasonCarriesTheDefaults()
    {
        Assert.Equal(LeagueRules.Default, Blank().Settings.Rules);
    }

    [Fact]
    public void AnyOneToOneMappingOfTheFourColorsIsAccepted()
    {
        var created = PointColors.Create(Colors((1, ColorTier.Red), (2, ColorTier.Yellow), (3, ColorTier.Blue), (4, ColorTier.Green)));

        Assert.True(created.IsSuccess);
        Assert.Equal(ColorTier.Red, created.Value!.For(1));
        Assert.Equal(ColorTier.Green, created.Value.For(4));
    }

    [Fact]
    public void TheOrderOfTheEntriesDoesNotMatter()
    {
        var created = PointColors.Create(Colors((4, ColorTier.Red), (2, ColorTier.Blue), (1, ColorTier.Green), (3, ColorTier.Yellow)));

        Assert.Equal(PointColors.Default, created.Value);
    }

    public static TheoryData<PointColor[]> BadMappings => new()
    {
        Colors((1, ColorTier.Green), (2, ColorTier.Blue), (3, ColorTier.Yellow)),
        Colors((1, ColorTier.Green), (2, ColorTier.Blue), (3, ColorTier.Yellow), (4, ColorTier.Red), (5, ColorTier.Green)),
        Colors((1, ColorTier.Green), (2, ColorTier.Green), (3, ColorTier.Yellow), (4, ColorTier.Red)),
        Colors((1, ColorTier.Green), (1, ColorTier.Blue), (3, ColorTier.Yellow), (4, ColorTier.Red)),
        Colors((0, ColorTier.Green), (2, ColorTier.Blue), (3, ColorTier.Yellow), (4, ColorTier.Red)),
        Colors((1, ColorTier.Green), (2, ColorTier.Blue), (3, ColorTier.Yellow), (5, ColorTier.Red)),
        Colors((1, (ColorTier)9), (2, ColorTier.Blue), (3, ColorTier.Yellow), (4, ColorTier.Red)),
        Array.Empty<PointColor>(),
    };

    [Theory]
    [MemberData(nameof(BadMappings))]
    public void AMappingThatIsNotExactlyFourPointsAndFourColorsOnceEachIsRejected(PointColor[] entries)
    {
        var created = PointColors.Create(entries);

        Assert.Equal("season.league-settings", created.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(created.Error.Message));
    }

    [Fact]
    public void AnUnknownRecordHolderModeIsRejected()
    {
        var created = LeagueRules.Create(true, PointColors.Default, (RecordHolderMode)7);

        Assert.Equal("season.league-settings", created.Error!.Code);
    }

    [Fact]
    public void SavingChangesOnlyTheRulesAndKeepsEveryOtherSetting()
    {
        var season = Blank();
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        var before = season.Settings;
        var rules = LeagueRules.Create(false, PointColors.Create(Colors((1, ColorTier.Blue), (2, ColorTier.Green), (3, ColorTier.Yellow), (4, ColorTier.Red))).Value!, RecordHolderMode.MostOccurrences).Value!;

        var saved = season.SaveLeagueRules(rules);

        Assert.True(saved.IsSuccess);
        Assert.Equal(rules, season.Settings.Rules);
        Assert.Equal(before with { Rules = rules }, season.Settings);
        Assert.Equal((before.BuyIn, before.DoubleDogWeeks, before.TeamPickDeadlineEastern), (season.Settings.BuyIn, season.Settings.DoubleDogWeeks, season.Settings.TeamPickDeadlineEastern));
    }

    [Fact]
    public void ALiveSeasonCannotChangeItsLeagueRules()
    {
        var season = Blank();
        Assert.True(season.ActivateWithDogs().IsSuccess);

        var saved = season.SaveLeagueRules(LeagueRules.Default with { TeamMayAlsoBeDog = false });

        Assert.Equal("season.locked", saved.Error!.Code);
        Assert.True(season.Settings.Rules.TeamMayAlsoBeDog);
    }

    [Fact]
    public void MoneyDeadlineAndDoubleDogEditsKeepTheRules()
    {
        var season = Blank();
        var rules = LeagueRules.Default with { TeamMayAlsoBeDog = false };
        Assert.True(season.SaveLeagueRules(rules).IsSuccess);

        Assert.True(season.UpdateMoney(new Money(150m, "USD"), new Money(90m, "USD"), new Money(30m, "USD"), new Money(15m, "USD"), new Money(15m, "USD")).IsSuccess);
        Assert.True(season.UpdateDeadline(new TimeOnly(11, 30)).IsSuccess);
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);

        Assert.Equal(rules, season.Settings.Rules);
    }
}
