using System.Text.Json.Serialization;
using Swan.Core.Common;

namespace Swan.Core.League;

// The four tiebreaker colors (R18). Strings in JSON so an API client reads "Green", not 0.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ColorTier { Green, Blue, Yellow, Red }

// Who holds a "Going Perf" or "0fer" record (R19). Saved now; the records feature that reads it comes later.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecordHolderMode { FirstOccurrence, MostOccurrences }

public sealed record PointColor(int Points, ColorTier Color);

// R18: each of the four point values (1 to 4) has its own color, and every color is used exactly once.
public sealed record PointColors(ColorTier ForOnePoint, ColorTier ForTwoPoints, ColorTier ForThreePoints, ColorTier ForFourPoints)
{
    public static readonly PointColors Default = new(ColorTier.Green, ColorTier.Blue, ColorTier.Yellow, ColorTier.Red);

    public IReadOnlyList<PointColor> Entries => [new(1, ForOnePoint), new(2, ForTwoPoints), new(3, ForThreePoints), new(4, ForFourPoints)];

    public ColorTier For(int points) => points switch
    {
        1 => ForOnePoint,
        2 => ForTwoPoints,
        3 => ForThreePoints,
        4 => ForFourPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(points), "Point values are 1 to 4."),
    };

    public static Result<PointColors> Create(IReadOnlyCollection<PointColor> entries)
    {
        if (entries.Count != 4 || entries.Select(entry => entry.Points).Order().SequenceEqual([1, 2, 3, 4]) is false)
            return Failure("Give each point value from 1 to 4 exactly one color.");
        if (entries.Any(entry => !Enum.IsDefined(entry.Color))) return Failure("Choose green, blue, yellow or red for each point value.");
        if (entries.Select(entry => entry.Color).Distinct().Count() != 4) return Failure("Each color can be used for only one point value.");
        var byPoints = entries.ToDictionary(entry => entry.Points, entry => entry.Color);
        return Result<PointColors>.Success(new(byPoints[1], byPoints[2], byPoints[3], byPoints[4]));
    }

    private static Result<PointColors> Failure(string message) => Result<PointColors>.Failure("season.league-settings", message);
}

// The three owner-controlled settings R17 to R19 (the other rules are fixed). Defaults: R17 on, green/blue/yellow/red, first occurrence.
public sealed record LeagueRules(bool TeamMayAlsoBeDog, PointColors PointColors, RecordHolderMode RecordHolder)
{
    public static readonly LeagueRules Default = new(true, PointColors.Default, RecordHolderMode.FirstOccurrence);

    public static Result<LeagueRules> Create(bool teamMayAlsoBeDog, PointColors pointColors, RecordHolderMode recordHolder) =>
        Enum.IsDefined(recordHolder)
            ? Result<LeagueRules>.Success(new(teamMayAlsoBeDog, pointColors, recordHolder))
            : Result<LeagueRules>.Failure("season.league-settings", "Choose how Going Perf and 0fer records are held.");
}
