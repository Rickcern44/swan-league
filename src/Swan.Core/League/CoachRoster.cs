using Swan.Core.Common;

namespace Swan.Core.League;

// One row of the coach list as submitted: nothing is trusted until CoachRoster.Validate has seen it.
public sealed record CoachEntry(string? Email, string? Name);

public sealed record ValidCoach(Email Email, string Name);

// The coach list an administrator prepares for a Setup season. Emails are compared trimmed and lowercase.
public static class CoachRoster
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 320;

    public static Result<IReadOnlyList<ValidCoach>> Validate(IReadOnlyList<CoachEntry> entries)
    {
        var valid = new List<ValidCoach>();
        for (var index = 0; index < entries.Count; index++)
        {
            var row = $"row {index + 1}";
            var rawEmail = entries[index].Email?.Trim().ToLowerInvariant() ?? "";
            if (rawEmail.Length is 0 or > MaxEmailLength) return Failure($"Coach {row}: enter a valid email address (up to {MaxEmailLength} characters).");
            var email = Email.Create(rawEmail);
            if (!email.IsSuccess) return Failure($"Coach {row}: enter a valid email address.");
            var name = entries[index].Name?.Trim() ?? "";
            if (name.Length is 0 or > MaxNameLength) return Failure($"Coach {row}: enter a display name (up to {MaxNameLength} characters).");
            if (valid.Any(other => other.Email == email.Value)) return Failure($"Coach {row}: {email.Value!.Value} is listed more than once.");
            valid.Add(new ValidCoach(email.Value!, name));
        }

        return Result<IReadOnlyList<ValidCoach>>.Success(valid);
    }

    private static Result<IReadOnlyList<ValidCoach>> Failure(string message) => Result<IReadOnlyList<ValidCoach>>.Failure("season.coaches", message);
}
