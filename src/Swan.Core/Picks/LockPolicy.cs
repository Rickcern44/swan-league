using Swan.Core.League;

namespace Swan.Core.Picks;

public static class LockPolicy
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeOnly DefaultDeadlineEastern = new(13, 0);

    public static DateTimeOffset TeamPickLockTime(DateTimeOffset kickoff, SeasonSettings settings, DateTimeOffset weekStartUtc) =>
        kickoff <= WeekDeadline(settings, weekStartUtc) ? kickoff : WeekDeadline(settings, weekStartUtc);

    public static DateTimeOffset DogPickLockTime(DateTimeOffset kickoff) => kickoff;

    public static bool CanChange(DateTimeOffset lockTime, DateTimeOffset now) => now < lockTime;

    public static DateTimeOffset WeekDeadline(SeasonSettings settings, DateTimeOffset weekStartUtc)
    {
        var weekStartDate = DateOnly.FromDateTime(weekStartUtc.UtcDateTime);
        var sunday = weekStartDate.AddDays(((int)DayOfWeek.Sunday - (int)weekStartDate.DayOfWeek + 7) % 7);
        var local = sunday.ToDateTime(settings.TeamPickDeadlineEastern ?? DefaultDeadlineEastern, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Eastern.GetUtcOffset(local)).ToUniversalTime();
    }
}
