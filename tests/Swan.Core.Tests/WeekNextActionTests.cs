using Swan.Core.Common;
using Swan.Core.Schedule;
using Xunit;

namespace Swan.Core.Tests;

public sealed class WeekNextActionTests
{
    private static WeekSnapshot Week(WeekState state, int included = 16, int lines = 0, int results = 0, int excluded = 0) =>
        new(new WeekId(Guid.NewGuid()), 4, state, WeekKind.Regular, included + excluded, included, lines, results, false);

    [Fact]
    public void ADraftWeekWithMissingLinesNamesWhatIsLeftAndOffersNothingToPress()
    {
        var action = WeekNextAction.For(Week(WeekState.Draft, lines: 13));

        Assert.Equal(("add-lines", false), (action.Kind, action.CanAct));
        Assert.Equal("3 of 16 included games still need an approved line.", action.Detail);
    }

    [Fact]
    public void ADraftWeekWithEveryLineApprovedCanApproveThem()
    {
        var action = WeekNextAction.For(Week(WeekState.Draft, included: 14, lines: 14, excluded: 2));

        Assert.Equal(("approve-lines", true, "Approve 14 lines"), (action.Kind, action.CanAct, action.Label));
    }

    [Fact]
    public void ADraftWeekWithNoIncludedGamesCannotMove()
    {
        Assert.Equal(("week-empty", false), (WeekNextAction.For(Week(WeekState.Draft, included: 0, excluded: 2)) is var action ? (action.Kind, action.CanAct) : default));
    }

    [Theory]
    [InlineData(WeekState.LinesApproved, "publish")]
    [InlineData(WeekState.Open, "lock")]
    [InlineData(WeekState.Final, "recompute")]
    public void EachSteadyStateOffersItsSingleTransition(WeekState state, string kind)
    {
        var action = WeekNextAction.For(Week(state, lines: 16, results: state == WeekState.Final ? 16 : 0));

        Assert.Equal((kind, true), (action.Kind, action.CanAct));
    }

    [Fact]
    public void ALockedWeekNeedsEveryResultBeforeItCanBeFinalized()
    {
        var missing = WeekNextAction.For(Week(WeekState.Locked, lines: 16, results: 14));
        var done = WeekNextAction.For(Week(WeekState.Locked, lines: 16, results: 16));

        Assert.Equal(("enter-results", false, "2 of 16 included games still need a result."), (missing.Kind, missing.CanAct, missing.Detail));
        Assert.Equal(("finalize", true), (done.Kind, done.CanAct));
    }
}
