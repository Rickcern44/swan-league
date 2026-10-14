namespace Swan.Core.Common;

// A schedule week as the holiday rule sees it: its number and the instants its games kick off.
public sealed record WeekKickoffs(int Number, IReadOnlyList<DateTimeOffset> Kickoffs);

public sealed record DoubleDogHolidays(DateOnly Thanksgiving, int? ThanksgivingWeek, DateOnly Christmas, int? ChristmasWeek);

// R7: Week 2 is always a double-dog week, with three or four in total. Thanksgiving and Christmas weeks are only suggestions.
public static class DoubleDogWeekRules
{
    public const int RegularSeasonWeeks = 18;

    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static Result<IReadOnlyList<int>> Validate(IReadOnlyCollection<int> weeks)
    {
        if (weeks.Any(week => week is < 1 or > RegularSeasonWeeks)) return Failure($"Double-dog weeks must be regular-season weeks, 1 to {RegularSeasonWeeks}.");
        if (weeks.Distinct().Count() != weeks.Count) return Failure("A week was chosen more than once; choose each double-dog week once.");
        if (weeks.Count is < 3 or > 4) return Failure($"Choose 3 or 4 double-dog weeks (you chose {weeks.Count}).");
        if (!weeks.Contains(2)) return Failure("Week 2 is always a double-dog week; include it.");
        return Result<IReadOnlyList<int>>.Success(weeks.Order().ToList());
    }

    // The week containing a date runs from its first game's Eastern date up to the next week's first game (up to its own last game, for the final week), so a holiday with no game that day still belongs to the week then running.
    public static DoubleDogHolidays Holidays(int year, IReadOnlyCollection<WeekKickoffs> weeks)
    {
        var thanksgiving = FourthThursdayOfNovember(year);
        var christmas = new DateOnly(year, 12, 25);
        var ordered = weeks.Where(week => week.Kickoffs.Count > 0)
            .Select(week => (week.Number, First: week.Kickoffs.Min(EasternDate), Last: week.Kickoffs.Max(EasternDate)))
            .OrderBy(week => week.First).ThenBy(week => week.Number).ToList();
        int? WeekContaining(DateOnly date)
        {
            for (var index = 0; index < ordered.Count; index++)
            {
                var (number, first, last) = ordered[index];
                var isLast = index == ordered.Count - 1;
                if (date >= first && (isLast ? date <= last : date < ordered[index + 1].First)) return number;
            }

            return null;
        }

        return new(thanksgiving, WeekContaining(thanksgiving), christmas, WeekContaining(christmas));
    }

    private static DateOnly EasternDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Eastern).DateTime);

    private static DateOnly FourthThursdayOfNovember(int year)
    {
        var first = new DateOnly(year, 11, 1);
        var firstThursday = first.AddDays(((int)DayOfWeek.Thursday - (int)first.DayOfWeek + 7) % 7);
        return firstThursday.AddDays(21);
    }

    private static Result<IReadOnlyList<int>> Failure(string message) => Result<IReadOnlyList<int>>.Failure("season.double-dogs", message);
}
