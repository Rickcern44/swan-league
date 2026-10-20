using Swan.Core.Common;

namespace Swan.Core.League;

// Something an administrator should look at, with the wizard step that fixes it.
public sealed record SetupIssue(string Code, string Message, SetupStep Step);

// Facts about the Setup season that live outside the Season aggregate, read from persisted data only.
public sealed record SetupFacts(int TeamsEntered, int TeamsTotal, bool TeamsComplete, int ScheduleWeeks, bool ScheduleComplete, int ActiveCoaches, IReadOnlyList<int> HolidayWeeksNotSelected, int? ScheduleYear = null);

// Whether a Setup season can be finished. Blockers stop it; warnings do not.
public sealed record SetupReadiness(IReadOnlyList<SetupIssue> Blockers, IReadOnlyList<SetupIssue> Warnings)
{
    public bool Ready => Blockers.Count == 0;

    public static SetupReadiness Evaluate(Season season, SetupFacts facts)
    {
        var blockers = new List<SetupIssue>();
        var warnings = new List<SetupIssue>();
        if (string.IsNullOrWhiteSpace(season.Name) || season.Year is < 2000 or > 2100)
            blockers.Add(new("basics", "The season needs a name and a year between 2000 and 2100.", SetupStep.Basics));
        var stakes = season.Pools.Select(pool => pool.Name).Order().ToList();
        if (!stakes.SequenceEqual(PayoutRules.Pools.Order()) || season.Pools.Any(pool => pool.Stake.Amount <= 0) || season.Pools.Sum(pool => pool.Stake.Amount) != season.Settings.BuyIn.Amount)
            blockers.Add(new("money", "The four pool stakes must each be above zero and add up to the buy-in.", SetupStep.Money));
        if (!facts.TeamsComplete)
            blockers.Add(new("teams", $"Teams and point values are incomplete: {facts.TeamsEntered} of {facts.TeamsTotal} teams have a division finish.", SetupStep.PointValues));
        if (!facts.ScheduleComplete)
            blockers.Add(new("schedule", $"The regular-season schedule is incomplete: {facts.ScheduleWeeks} of {DoubleDogWeekRules.RegularSeasonWeeks} weeks are imported.", SetupStep.Schedule));
        var dogs = DoubleDogWeekRules.Validate(season.DoubleDogWeekNumbers);
        if (!dogs.IsSuccess || season.Settings.DoubleDogWeeks != season.DoubleDogWeekNumbers.Count)
            blockers.Add(new("double-dogs", season.DoubleDogWeekNumbers.Count == 0 ? "Choose the 3 or 4 double-dog weeks (Week 2 plus the others)." : $"The saved double-dog weeks are not valid: {dogs.Error?.Message}", SetupStep.Schedule));
        var fine = season.MissedPickFine is null ? null : PayoutRules.ValidateFine(season.MissedPickFine);
        var plans = season.PayoutPlans.Count == 0 ? null : PayoutRules.Validate(season.PayoutPlans);
        if (fine is not { IsSuccess: true } || plans is not { IsSuccess: true })
            blockers.Add(new("payouts", fine is null || plans is null ? "Save the missed-pick fine and each pool's payout plan." : $"The saved fine or payout plans are not valid: {(fine is { IsSuccess: false } ? fine.Error!.Message : plans!.Error!.Message)}", SetupStep.Money));
        if (facts.ActiveCoaches == 0)
            warnings.Add(new("no-coaches", "No coaches have been added yet. You can finish setup now and add them later.", SetupStep.Coaches));
        if (facts.ScheduleYear is { } scheduleYear && scheduleYear != season.Year)
            warnings.Add(new("year-mismatch", $"The season year is {season.Year} but the imported schedule is for {scheduleYear}. Check the year on the Basics page.", SetupStep.Basics));
        if (facts.HolidayWeeksNotSelected.Count > 0)
            warnings.Add(new("holiday-weeks", $"{string.Join(" and ", facts.HolidayWeeksNotSelected.Select(week => $"Week {week}"))} contain{(facts.HolidayWeeksNotSelected.Count == 1 ? "s" : "")} a holiday the schedule suggests as a double-dog week but {(facts.HolidayWeeksNotSelected.Count == 1 ? "is" : "are")} not selected.", SetupStep.Schedule));
        return new SetupReadiness(blockers, warnings);
    }
}
