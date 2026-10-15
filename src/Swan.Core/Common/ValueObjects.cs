using System.Net.Mail;

namespace Swan.Core.Common;

public sealed record Money(decimal Amount, string Currency)
{
    public static Result<Money> Create(decimal amount, string currency) =>
        amount < 0 || currency.Length != 3 || !currency.All(char.IsAsciiLetter)
            ? Result<Money>.Failure("money.invalid", "Money must be non-negative with an ISO currency code.")
            : Result<Money>.Success(new(amount, currency.ToUpperInvariant()));
}

public sealed record PointValue(int Value)
{
    public static Result<PointValue> Create(int value) => value is < 1 or > 4
        ? Result<PointValue>.Failure("point-value.invalid", "Point value must be between 1 and 4.")
        : Result<PointValue>.Success(new(value));
}

public sealed record Spread(decimal Value)
{
    // No NFL spread comes near 30, so a larger number is a typo (such as -35 for -3.5) rather than a line.
    public const decimal MaxMagnitude = 30m;
    public static Result<Spread> Create(decimal value) => value < -MaxMagnitude || value > MaxMagnitude
        ? Result<Spread>.Failure("spread.invalid", "A spread must be between -30 and 30.")
        : Result<Spread>.Success(new(value));
}

public sealed record DogPoints(decimal Value, int Rounded)
{
    public static Result<DogPoints> Create(decimal value) => value < 0
        ? Result<DogPoints>.Failure("dog-points.invalid", "Dog points cannot be negative.")
        : Result<DogPoints>.Success(new(value, decimal.ToInt32(decimal.Ceiling(value))));
}

public sealed record Kickoff(DateTimeOffset Value)
{
    public static Result<Kickoff> Create(DateTimeOffset value) => value.Offset != TimeSpan.Zero
        ? Result<Kickoff>.Failure("kickoff.invalid", "Kickoff must be UTC.")
        : Result<Kickoff>.Success(new(value));
}

public enum GameWinner { Home, Away, Tie }
public sealed record GameResult(int HomeScore, int AwayScore)
{
    public GameWinner Winner => HomeScore == AwayScore ? GameWinner.Tie : HomeScore > AwayScore ? GameWinner.Home : GameWinner.Away;
    public const int MaxScore = 100;
    public static Result<GameResult> Create(int homeScore, int awayScore) => homeScore < 0 || awayScore < 0 || homeScore > MaxScore || awayScore > MaxScore
        ? Result<GameResult>.Failure("game-result.invalid", "Scores must be between 0 and 100.")
        : Result<GameResult>.Success(new(homeScore, awayScore));
}

public sealed record Email(string Value)
{
    public static Result<Email> Create(string value)
    {
        try { var address = new MailAddress(value); return address.Address == value ? Result<Email>.Success(new(value)) : Result<Email>.Failure("email.invalid", "Email is malformed."); }
        catch (FormatException) { return Result<Email>.Failure("email.invalid", "Email is malformed."); }
    }
}

public enum Audience { All, Coaches, Administrators, Individual }
