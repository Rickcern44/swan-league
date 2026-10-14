using Swan.Core.Common;
using Swan.Core.Integrations;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class ScheduleImportPlanTests
{
    private static readonly SeasonId Season = new(Guid.NewGuid());
    private static readonly Team Bills = new(new TeamId(Guid.NewGuid()), "Buffalo Bills", null, DateTimeOffset.UtcNow, "AFC East");
    private static readonly Team Jets = new(new TeamId(Guid.NewGuid()), "New York Jets", null, DateTimeOffset.UtcNow, "AFC East");
    private static readonly Team Giants = new(new TeamId(Guid.NewGuid()), "New York Giants", null, DateTimeOffset.UtcNow, "NFC East");
    private static readonly Team[] Catalog = [Bills, Jets, Giants];
    private static readonly HashSet<TeamId> InSeason = [Bills.Id, Jets.Id];
    private static readonly DateTimeOffset Sunday = new(2026, 9, 13, 17, 0, 0, TimeSpan.Zero);

    private static ProviderGame Game(string id = "1", int week = 1, string home = "Buffalo Bills", string away = "New York Jets", DateTimeOffset? kickoff = null, bool noKickoff = false) =>
        new(id, week, home, away, noKickoff ? null : kickoff ?? Sunday);

    private static Result<IReadOnlyList<Week>> Build(params ProviderGame[] games) => ScheduleImportPlan.Build(Season, games, Catalog, InSeason);

    // The given games plus one filler game in every regular-season week they leave out, so the schedule is complete.
    private static ProviderGame[] Full(params ProviderGame[] games) => games
        .Concat(Enumerable.Range(1, 18).Where(week => games.All(game => game.Week != week)).Select(week => Game($"filler-{week}", week: week, kickoff: Sunday.AddDays((week - 1) * 7))))
        .ToArray();

    [Theory]
    [InlineData(18)]
    [InlineData(1)]
    [InlineData(9)]
    public void AScheduleMissingAnyRegularSeasonWeekIsRejectedNamingIt(int missing)
    {
        var games = Enumerable.Range(1, 18).Where(week => week != missing).Select(week => Game($"g{week}", week: week)).ToArray();

        var built = Build(games);

        Assert.Equal("schedule.invalid", built.Error!.Code);
        Assert.Contains($"week {missing}", built.Error.Message);
    }

    [Fact]
    public void ASeasonWithOnlySomeWeeksListsEveryMissingOne()
    {
        var built = Build(Game("a", week: 1), Game("b", week: 2));

        Assert.Equal("schedule.invalid", built.Error!.Code);
        Assert.Contains("3", built.Error.Message);
        Assert.Contains("18", built.Error.Message);
    }

    [Fact]
    public void ACompleteSeasonOfExactlyEighteenWeeksIsAccepted()
    {
        var built = Build(Full());

        Assert.Equal(Enumerable.Range(1, 18), built.Value!.Select(week => week.Number));
    }

    [Fact]
    public void ValidGamesBecomeRegularWeeksInOrderOwningGamesWithProviderIdsAndUtcKickoffs()
    {
        var built = Build(Full(Game("20", week: 2, kickoff: Sunday.AddDays(7)), Game("10", week: 1, kickoff: new DateTimeOffset(2026, 9, 13, 13, 0, 0, TimeSpan.FromHours(-4)))));

        Assert.True(built.IsSuccess);
        Assert.Equal(Enumerable.Range(1, 18), built.Value!.Select(week => week.Number));
        Assert.All(built.Value, week => Assert.Equal((Season, WeekKind.Regular, WeekState.Draft, true), (week.SeasonId, week.Type.Kind, week.State, week.Type.PicksRequired)));
        var game = built.Value[0].Games.Single();
        Assert.Equal(("10", Bills.Id, Jets.Id), (game.ProviderGameId, game.HomeTeamId, game.AwayTeamId));
        Assert.Equal((Sunday, TimeSpan.Zero), (game.Kickoff.Value, game.Kickoff.Value.Offset));
    }

    [Fact]
    public void NoGamesIsRejectedAsEmpty()
    {
        Assert.Equal("schedule.empty", Build().Error!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(-1)]
    public void AWeekOutsideTheRegularSeasonIsRejected(int week)
    {
        var built = Build(Game(week: week));

        Assert.Equal("schedule.invalid", built.Error!.Code);
        Assert.Contains("unexpected week", built.Error.Message);
    }

    [Fact]
    public void ADuplicateProviderGameIdIsRejected()
    {
        Assert.Contains("more than once", Build(Game("7"), Game("7", week: 2)).Error!.Message);
    }

    [Fact]
    public void AMissingProviderGameIdIsRejected()
    {
        Assert.Equal("schedule.invalid", Build(Game(" ")).Error!.Code);
    }

    [Theory]
    [InlineData("Nowhere Navigators", "New York Jets")]
    [InlineData("Buffalo Bills", "Nowhere Navigators")]
    public void AnUnknownTeamIsRejectedByName(string home, string away)
    {
        var built = Build(Game(home: home, away: away));

        Assert.Equal("schedule.invalid", built.Error!.Code);
        Assert.Contains("Nowhere Navigators", built.Error.Message);
    }

    [Fact]
    public void ATeamThatIsNotInTheSetupSeasonIsRejected()
    {
        var built = Build(Game(away: "New York Giants"));

        Assert.Equal("schedule.invalid", built.Error!.Code);
        Assert.Contains("New York Giants is not in this season's team list", built.Error.Message);
    }

    [Fact]
    public void AnUnreadableKickoffIsRejected()
    {
        Assert.Contains("unreadable kickoff", Build(Game(noKickoff: true)).Error!.Message);
    }

    [Fact]
    public void ATeamPlayingItselfIsRejected()
    {
        Assert.Equal("schedule.invalid", Build(Game(away: "Buffalo Bills")).Error!.Code);
    }

    [Fact]
    public void TheFingerprintIgnoresOrderButNotAnyGameDetail()
    {
        var first = Build(Full(Game("1"), Game("2", week: 2))).Value!;
        var reordered = Build(Full(Game("2", week: 2), Game("1"))).Value!;
        var moved = Build(Full(Game("1"), Game("2", week: 2, kickoff: Sunday.AddDays(8)))).Value!;
        var swapped = Build(Full(Game("1", home: "New York Jets", away: "Buffalo Bills"), Game("2", week: 2))).Value!;

        Assert.Equal(ScheduleImportPlan.Fingerprint(first), ScheduleImportPlan.Fingerprint(reordered));
        Assert.NotEqual(ScheduleImportPlan.Fingerprint(first), ScheduleImportPlan.Fingerprint(moved));
        Assert.NotEqual(ScheduleImportPlan.Fingerprint(first), ScheduleImportPlan.Fingerprint(swapped));
    }
}
