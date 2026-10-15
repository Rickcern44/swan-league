namespace Swan.Core.Common;

// The league runs on Eastern Time. A commissioner types a wall-clock time; the instant that is stored is unambiguous.
public static class EasternTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // A wall-clock time that the clocks skip (spring forward) or repeat (fall back) has no single meaning, so it is refused rather than guessed.
    public static Result<DateTimeOffset> ToInstant(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(unspecified)) return Result<DateTimeOffset>.Failure("season.draft-deadline", "That Eastern time does not exist because the clocks skip it. Choose another time.");
        if (Zone.IsAmbiguousTime(unspecified)) return Result<DateTimeOffset>.Failure("season.draft-deadline", "That Eastern time happens twice because the clocks repeat it. Choose another time.");
        return Result<DateTimeOffset>.Success(new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified)).ToUniversalTime());
    }

    public static string ToLocalText(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).ToString("yyyy-MM-dd'T'HH:mm");
}
