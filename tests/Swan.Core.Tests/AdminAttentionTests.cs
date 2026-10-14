using Swan.Core.Common;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class AdminAttentionTests
{
    private static WeekSnapshot Week(int number, WeekState state, int included = 16, int lines = 0, int results = 0, int excluded = 0, bool doubleDog = false, WeekKind kind = WeekKind.Regular) =>
        new(new WeekId(Guid.NewGuid()), number, state, kind, included + excluded, included, lines, results, doubleDog);

    private static string[] Kinds(AdminAttention attention) => attention.Items.Select(item => item.Kind).ToArray();

    [Fact]
    public void ADraftWeekWithGamesMissingLinesAsksForThemWithTheCount()
    {
        var attention = AdminAttention.Derive([Week(4, WeekState.Draft, included: 16, lines: 13)]);

        var item = Assert.Single(attention.Items);
        Assert.Equal(("lines-missing", 4, WeekState.Draft, 3, 16), (item.Kind, item.WeekNumber, item.WeekState, item.Count, item.Of));
        Assert.Equal("3 of 16 included games need a line", item.Reason);
        Assert.False(attention.CaughtUp);
    }

    [Fact]
    public void ExcludedGamesNeverCountTowardsMissingLines()
    {
        var attention = AdminAttention.Derive([Week(4, WeekState.Draft, included: 14, lines: 14, excluded: 2)]);

        var item = Assert.Single(attention.Items);
        Assert.Equal("lines-ready", item.Kind);
        Assert.Contains("14", item.Reason);
    }

    [Fact]
    public void ReasonsReadCorrectlyForASingleGame()
    {
        Assert.Equal("1 of 1 included game needs a line", Assert.Single(AdminAttention.Derive([Week(1, WeekState.Draft, included: 1)]).Items).Reason);
        Assert.Equal("The included game has an approved line; approve the week's lines.", Assert.Single(AdminAttention.Derive([Week(1, WeekState.Draft, included: 1, lines: 1)]).Items).Reason);
        Assert.Equal("1 of 1 included game is missing a result", Assert.Single(AdminAttention.Derive([Week(1, WeekState.Locked, included: 1, lines: 1)]).Items).Reason);
        Assert.Equal("1 of 3 included games needs a line", Assert.Single(AdminAttention.Derive([Week(1, WeekState.Draft, included: 3, lines: 2)]).Items).Reason);
    }

    [Fact]
    public void ADraftWeekWithNoIncludedGamesSaysSo()
    {
        var attention = AdminAttention.Derive([Week(4, WeekState.Draft, included: 0, excluded: 3)]);

        Assert.Equal(["week-empty"], Kinds(attention));
    }

    [Fact]
    public void OnlyTheNextDraftWeekNeedsLinesNotEveryFutureDraft()
    {
        var weeks = Enumerable.Range(4, 15).Select(number => Week(number, WeekState.Draft)).ToArray();

        var attention = AdminAttention.Derive(weeks);

        var item = Assert.Single(attention.Items);
        Assert.Equal(4, item.WeekNumber);
    }

    [Fact]
    public void ALinesApprovedWeekIsReadyToPublish()
    {
        var attention = AdminAttention.Derive([Week(5, WeekState.LinesApproved, lines: 16)]);

        var item = Assert.Single(attention.Items);
        Assert.Equal(("publish-ready", 5), (item.Kind, item.WeekNumber));
        Assert.Contains("publish", item.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOpenWeekIsInformationalPicksLive()
    {
        var attention = AdminAttention.Derive([Week(5, WeekState.Open, lines: 16)]);

        var item = Assert.Single(attention.Items);
        Assert.Equal(("picks-live", 5), (item.Kind, item.WeekNumber));
        Assert.False(item.NeedsDecision);
    }

    [Fact]
    public void ALockedWeekMissingResultsCountsThemAndOneWithAllResultsIsReadyToFinalize()
    {
        var missing = AdminAttention.Derive([Week(3, WeekState.Locked, included: 16, lines: 16, results: 14)]);
        var done = AdminAttention.Derive([Week(3, WeekState.Locked, included: 16, lines: 16, results: 16)]);

        var item = Assert.Single(missing.Items);
        Assert.Equal(("results-missing", 2, 16), (item.Kind, item.Count, item.Of));
        Assert.Equal("2 of 16 included games are missing results", item.Reason);
        Assert.Equal(["finalize-ready"], Kinds(done));
    }

    [Fact]
    public void AnEarlierNonFinalWeekOutranksTheNewerCurrentWeek()
    {
        var attention = AdminAttention.Derive([
            Week(2, WeekState.Final, lines: 16, results: 16),
            Week(3, WeekState.Locked, lines: 16, results: 14),
            Week(4, WeekState.Draft, lines: 13),
            Week(5, WeekState.Draft),
        ]);

        Assert.Equal(["results-missing", "lines-missing"], Kinds(attention));
        Assert.Equal([3, 4], attention.Items.Select(item => item.WeekNumber));
        Assert.Equal(4, attention.CurrentWeek!.Number);
    }

    [Fact]
    public void DecisionsOutrankInformationalItemsEvenWhenTheInformationalWeekIsEarlier()
    {
        var attention = AdminAttention.Derive([
            Week(3, WeekState.Open, lines: 16),
            Week(4, WeekState.Draft, lines: 0),
        ]);

        Assert.Equal(["lines-missing", "picks-live"], Kinds(attention));
        Assert.Equal([true, false], attention.Items.Select(item => item.NeedsDecision));
        Assert.Equal(3, attention.CurrentWeek!.Number);
    }

    [Fact]
    public void ASeasonWhereEveryWeekIsFinalIsCaughtUpWithNoFabricatedTask()
    {
        var attention = AdminAttention.Derive([Week(1, WeekState.Final, lines: 16, results: 16), Week(2, WeekState.Final, lines: 16, results: 16)]);

        Assert.Empty(attention.Items);
        Assert.True(attention.CaughtUp);
        Assert.Equal(2, attention.CurrentWeek!.Number);
    }

    [Fact]
    public void WithNoWeeksThereIsNoCurrentWeekAndNothingToDo()
    {
        var attention = AdminAttention.Derive([]);

        Assert.Null(attention.CurrentWeek);
        Assert.Empty(attention.Items);
        Assert.False(attention.CaughtUp);
    }

    [Fact]
    public void TheCurrentWeekIsTheLowestDraftLinesApprovedOrOpenWeekElseTheLowestLockedElseTheNewestFinal()
    {
        Assert.Equal(5, AdminAttention.Derive([Week(3, WeekState.Locked, results: 1), Week(5, WeekState.Draft), Week(6, WeekState.Draft)]).CurrentWeek!.Number);
        Assert.Equal(3, AdminAttention.Derive([Week(3, WeekState.Locked, results: 1), Week(4, WeekState.Locked, results: 1)]).CurrentWeek!.Number);
        Assert.Equal(7, AdminAttention.Derive([Week(6, WeekState.Final), Week(7, WeekState.Final)]).CurrentWeek!.Number);
    }

    [Fact]
    public void ThePlayoffAndDoubleDogDetailsRideAlongOnTheCurrentWeek()
    {
        var attention = AdminAttention.Derive([Week(19, WeekState.Draft, included: 6, kind: WeekKind.Playoff, doubleDog: false), Week(2, WeekState.Final, doubleDog: true)]);

        Assert.Equal((19, WeekKind.Playoff, 6, 6), (attention.CurrentWeek!.Number, attention.CurrentWeek.Kind, attention.CurrentWeek.IncludedGames, attention.CurrentWeek.Games));
        Assert.False(attention.CurrentWeek.IsDoubleDogWeek);
    }

    [Fact]
    public void TheOrderDoesNotDependOnTheOrderTheWeeksAreSupplied()
    {
        var weeks = new[] { Week(4, WeekState.Draft, lines: 13), Week(3, WeekState.Locked, lines: 16, results: 14), Week(1, WeekState.Final), Week(2, WeekState.Open, lines: 16) };

        var forward = AdminAttention.Derive(weeks);
        var reversed = AdminAttention.Derive(weeks.Reverse().ToArray());

        Assert.Equal(forward.Items.Select(item => (item.Kind, item.WeekNumber)), reversed.Items.Select(item => (item.Kind, item.WeekNumber)));
        Assert.Equal(forward.CurrentWeek!.Number, reversed.CurrentWeek!.Number);
        Assert.Equal(["results-missing", "lines-missing", "picks-live"], Kinds(forward));
    }
}
