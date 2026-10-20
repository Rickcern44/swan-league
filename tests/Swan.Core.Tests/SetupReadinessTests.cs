using Swan.Core.Common;
using Swan.Core.League;
using Xunit;

namespace Swan.Core.Tests;

public sealed class SetupReadinessTests
{
    private static readonly SetupFacts Complete = new(TeamsEntered: 32, TeamsTotal: 32, TeamsComplete: true, ScheduleWeeks: 18, ScheduleComplete: true, ActiveCoaches: 6, HolidayWeeksNotSelected: []);

    private static PayoutPlan[] Plans() => PayoutRules.Pools.Select(pool => new PayoutPlan(pool, [new PayoutPlace(1, 100m)])).ToArray();

    // A Setup season with everything a finished setup needs.
    private static Season Ready()
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        Assert.True(season.SaveFinesAndPayouts(new Money(5m, "USD"), Plans()).IsSuccess);
        return season;
    }

    private static string[] Codes(IEnumerable<SetupIssue> issues) => issues.Select(issue => issue.Code).ToArray();

    [Fact]
    public void AFullyConfiguredSetupIsReadyWithNoBlockersAndNoWarnings()
    {
        var readiness = SetupReadiness.Evaluate(Ready(), Complete);

        Assert.True(readiness.Ready);
        Assert.Empty(readiness.Blockers);
        Assert.Empty(readiness.Warnings);
    }

    [Fact]
    public void ABlankSeasonIsBlockedOnEverythingItHasNotChosenYetEachPointingAtItsStep()
    {
        var blank = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!;
        var facts = new SetupFacts(0, 32, false, 0, false, 0, []);

        var readiness = SetupReadiness.Evaluate(blank, facts);

        Assert.False(readiness.Ready);
        Assert.Equal(["teams", "schedule", "double-dogs", "payouts"], Codes(readiness.Blockers));
        Assert.Equal([SetupStep.PointValues, SetupStep.Schedule, SetupStep.Schedule, SetupStep.Money], readiness.Blockers.Select(issue => issue.Step));
        Assert.All(readiness.Blockers, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
        Assert.Contains("0 of 32", readiness.Blockers[0].Message);
        Assert.Contains("0 of 18", readiness.Blockers[1].Message);
    }

    [Fact]
    public void IncompleteTeamsBlock()
    {
        var readiness = SetupReadiness.Evaluate(Ready(), Complete with { TeamsEntered = 31, TeamsComplete = false });

        var blocker = Assert.Single(readiness.Blockers);
        Assert.Equal(("teams", SetupStep.PointValues), (blocker.Code, blocker.Step));
        Assert.Contains("31 of 32", blocker.Message);
    }

    [Fact]
    public void AnIncompleteScheduleBlocks()
    {
        var readiness = SetupReadiness.Evaluate(Ready(), Complete with { ScheduleWeeks = 17, ScheduleComplete = false });

        var blocker = Assert.Single(readiness.Blockers);
        Assert.Equal(("schedule", SetupStep.Schedule), (blocker.Code, blocker.Step));
        Assert.Contains("17 of 18", blocker.Message);
    }

    [Fact]
    public void ALegacyCountWithoutASavedDoubleDogSelectionBlocks()
    {
        var buyIn = new Money(100m, "USD");
        var legacy = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", new Money(60m, "USD")), new PoolDefinition("Dog", new Money(20m, "USD")), new PoolDefinition("Regular Season", new Money(10m, "USD")), new PoolDefinition("Playoff", new Money(10m, "USD"))]);
        Assert.True(legacy.SaveFinesAndPayouts(new Money(5m, "USD"), Plans()).IsSuccess);

        var readiness = SetupReadiness.Evaluate(legacy, Complete);

        Assert.Contains("double-dogs", Codes(readiness.Blockers));
    }

    [Fact]
    public void MissingOrUnsavedPayoutSettingsBlock()
    {
        var unconfigured = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(new Money(100m, "USD"), 3), [new PoolDefinition("Main", new Money(60m, "USD")), new PoolDefinition("Dog", new Money(20m, "USD")), new PoolDefinition("Regular Season", new Money(10m, "USD")), new PoolDefinition("Playoff", new Money(10m, "USD"))]);
        Assert.True(unconfigured.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);

        var readiness = SetupReadiness.Evaluate(unconfigured, Complete);

        // A season built with the raw constructor has no name or year either, so basics blocks as well.
        Assert.Equal(["basics", "payouts"], Codes(readiness.Blockers));
        Assert.Equal(SetupStep.Money, readiness.Blockers.Single(issue => issue.Code == "payouts").Step);
    }

    [Fact]
    public void PoolStakesThatDoNotAddUpToTheBuyInBlockOnTheMoneyStep()
    {
        var buyIn = new Money(100m, "USD");
        var off = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", new Money(60m, "USD")), new PoolDefinition("Dog", new Money(20m, "USD")), new PoolDefinition("Regular Season", new Money(10m, "USD")), new PoolDefinition("Playoff", new Money(5m, "USD"))]);
        Assert.True(off.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        Assert.True(off.SaveFinesAndPayouts(new Money(5m, "USD"), Plans()).IsSuccess);

        var readiness = SetupReadiness.Evaluate(off, Complete);

        Assert.Equal(["basics", "money"], Codes(readiness.Blockers));
        Assert.Equal(SetupStep.Money, readiness.Blockers.Single(issue => issue.Code == "money").Step);
    }

    [Fact]
    public void ALegacySeasonWithNoPoolsOrNameBlocksOnMoneyAndBasics()
    {
        var legacy = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(new Money(100m, "USD"), 3), []);
        Assert.True(legacy.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        Assert.True(legacy.SaveFinesAndPayouts(new Money(5m, "USD"), Plans()).IsSuccess);

        var readiness = SetupReadiness.Evaluate(legacy, Complete);

        Assert.Equal(["basics", "money"], Codes(readiness.Blockers));
    }

    [Fact]
    public void ZeroCoachesIsAWarningNotABlocker()
    {
        var readiness = SetupReadiness.Evaluate(Ready(), Complete with { ActiveCoaches = 0 });

        Assert.True(readiness.Ready);
        var warning = Assert.Single(readiness.Warnings);
        Assert.Equal(("no-coaches", SetupStep.Coaches), (warning.Code, warning.Step));
    }

    [Fact]
    public void AHolidayWeekThatIsNotADoubleDogWeekIsAWarningNamingIt()
    {
        var readiness = SetupReadiness.Evaluate(Ready(), Complete with { HolidayWeeksNotSelected = [13] });

        Assert.True(readiness.Ready);
        var warning = Assert.Single(readiness.Warnings);
        Assert.Equal(("holiday-weeks", SetupStep.Schedule), (warning.Code, warning.Step));
        Assert.Contains("Week 13", warning.Message);
    }

    [Fact]
    public void CompletingRecordsTheFinalStateAndNothingElseAndIsRepeatable()
    {
        var season = Ready();
        var settings = season.Settings;

        Assert.True(season.CompleteSetup().IsSuccess);
        Assert.True(season.CompleteSetup().IsSuccess);

        Assert.Equal(SetupStep.Completed, season.SetupStep);
        Assert.Equal((SeasonState.Setup, settings), (season.State, season.Settings));
    }

    [Fact]
    public void ALiveSeasonCannotBeCompleted()
    {
        var season = Ready();
        Assert.True(season.Activate().IsSuccess);

        var completed = season.CompleteSetup();

        Assert.Equal("season.locked", completed.Error!.Code);
        Assert.NotEqual(SetupStep.Completed, season.SetupStep);
    }

    [Fact]
    public void ALowerStepSavedAfterCompletionIsIgnoredSoCompletedIsNeverLostToAStaleCaller()
    {
        var season = Ready();
        Assert.True(season.CompleteSetup().IsSuccess);

        Assert.True(season.SaveSetupStep(SetupStep.Money).IsSuccess);
        Assert.True(season.SaveSetupStep(SetupStep.Review).IsSuccess);
        Assert.True(season.SaveSetupStep(SetupStep.Basics).IsSuccess);

        Assert.Equal(SetupStep.Completed, season.SetupStep);
    }

    [Fact]
    public void TheYearThatDoesNotMatchTheImportedScheduleIsAWarning()
    {
        var readiness = SetupReadiness.Evaluate(Ready(), Complete with { ScheduleYear = 2025 });

        Assert.True(readiness.Ready);
        var warning = Assert.Single(readiness.Warnings);
        Assert.Equal(("year-mismatch", SetupStep.Basics), (warning.Code, warning.Step));
        Assert.Contains("2026", warning.Message);
        Assert.Contains("2025", warning.Message);
    }

    [Fact]
    public void AMatchingOrUnknownScheduleYearRaisesNoWarning()
    {
        Assert.Empty(SetupReadiness.Evaluate(Ready(), Complete with { ScheduleYear = 2026 }).Warnings);
        Assert.Empty(SetupReadiness.Evaluate(Ready(), Complete with { ScheduleYear = null }).Warnings);
    }

    [Fact]
    public void TheCompletedStateCanOnlyBeReachedByCompletingNotBySavingAStep()
    {
        var season = Ready();

        var saved = season.SaveSetupStep(SetupStep.Completed);

        Assert.Equal("season.step", saved.Error!.Code);
        Assert.NotEqual(SetupStep.Completed, season.SetupStep);
        Assert.True(season.SaveSetupStep(SetupStep.Review).IsSuccess);
    }
}
