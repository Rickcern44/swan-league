using Swan.Core.Common;

namespace Swan.Core.League;

public enum SeasonState { Setup, Live, Completed }
// Where the Setup wizard resumes; ReviewAndCreate is the first step that is not built yet, so it means every implemented step is done.
public enum SetupStep { Basics, Money, WeeklyDeadline, TeamsAndPoints, ScheduleImport, WeeksAndDogs, FinesAndPayouts, LeagueSettings, Coaches, ReviewAndCreate }
public enum MembershipRole { Commissioner, Administrator, Coach }
public sealed record SeasonSettings(Money BuyIn, int DoubleDogWeeks, TimeOnly? TeamPickDeadlineEastern = null) : ITeamPickLockSettings
{
    // R17 to R19. A season stored before they existed has the defaults.
    public LeagueRules Rules { get; init; } = LeagueRules.Default;
}
public sealed record PlayoffRules(bool PicksRequired);
public sealed record RosterWindow(DateTimeOffset OpensAt, DateTimeOffset ClosesAt);
public sealed record PoolDefinition(string Name, Money Stake);
public sealed record Ruling(string Decision, DateTimeOffset DecidedAt);

public sealed class Season(SeasonId id, SeasonSettings initialSettings, IReadOnlyCollection<PoolDefinition> pools) : AggregateRoot
{
    private Season() : this(default, new SeasonSettings(new Money(0, "USD"), 3), []) { }
    public SeasonId Id { get; } = id;
    public SeasonSettings Settings { get; private set; } = initialSettings;
    public SeasonState State { get; private set; } = SeasonState.Setup;
    public int Year { get; private set; }
    public string Name { get; private set; } = "";
    public bool IsSimulation { get; private set; }
    public SetupStep SetupStep { get; private set; } = SetupStep.Basics;
    // The confirmed double-dog week numbers (R7); empty until an administrator saves a selection. Settings.DoubleDogWeeks is kept equal to its count once one exists.
    public IReadOnlyList<int> DoubleDogWeekNumbers { get; private set; } = [];
    // The fine for a missed team pick (USD) and how each pool pays out. A new season starts with a $5.00 fine and no plans; null / empty means a season that was never configured, which is not given invented choices.
    public Money? MissedPickFine { get; private set; }
    public IReadOnlyList<PayoutPlan> PayoutPlans { get; private set; } = [];
    public IReadOnlyList<PoolDefinition> Pools { get; private set; } = pools.ToList();

    // A blank season carries only the PRD defaults (W2): $100 buy-in split Main 60 / Dog 20 / Regular Season 10 / Playoff 10, three double-dog weeks, Sunday 1:00 PM ET deadline.
    public static Result<Season> CreateBlank(SeasonId id, int year, string name, bool isSimulation)
    {
        if (ValidateBasics(year, name) is { } invalid) return Result<Season>.Failure(invalid.Code, invalid.Message);
        var buyIn = new Money(100m, "USD");
        PoolDefinition[] pools = [new("Main", new Money(60m, "USD")), new("Dog", new Money(20m, "USD")), new("Regular Season", new Money(10m, "USD")), new("Playoff", new Money(10m, "USD"))];
        return Result<Season>.Success(new Season(id, new SeasonSettings(buyIn, 3, new TimeOnly(13, 0)), pools) { Year = year, Name = name.Trim(), IsSimulation = isSimulation, MissedPickFine = new Money(5.00m, "USD") });
    }

    // Only a Setup season's identity may be revised; pools, settings and state are never touched here.
    public Result<bool> UpdateBasics(int year, string name, bool isSimulation)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        if (ValidateBasics(year, name) is { } invalid) return Result<bool>.Failure(invalid.Code, invalid.Message);
        (Year, Name, IsSimulation) = (year, name.Trim(), isSimulation);
        return Result<bool>.Success(true);
    }

    // Money is the buy-in plus the four fixed pool stakes (names and order are canonical); the stakes must add up to the buy-in exactly.
    public Result<bool> UpdateMoney(Money buyIn, Money main, Money dog, Money regularSeason, Money playoff)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        Money[] stakes = [main, dog, regularSeason, playoff];
        if (buyIn.Currency != "USD" || stakes.Any(stake => stake.Currency != "USD")) return Result<bool>.Failure("season.money", "Buy-in and stakes must be in USD.");
        if (!IsValidAmount(buyIn) || stakes.Any(stake => !IsValidAmount(stake))) return Result<bool>.Failure("season.money", "Buy-in and every pool stake must be greater than zero and in whole cents.");
        // Subtracting from the buy-in, rather than summing the stakes, cannot overflow decimal however large the amounts are.
        var remaining = buyIn.Amount;
        foreach (var stake in stakes)
        {
            if (stake.Amount > remaining) return Result<bool>.Failure("season.money", "Pool stakes must add up to the buy-in.");
            remaining -= stake.Amount;
        }

        if (remaining != 0) return Result<bool>.Failure("season.money", "Pool stakes must add up to the buy-in.");
        Settings = Settings with { BuyIn = buyIn };
        Pools = [new("Main", main), new("Dog", dog), new("Regular Season", regularSeason), new("Playoff", playoff)];
        return Result<bool>.Success(true);
    }

    // The Sunday team-pick deadline, in Eastern Time; LockPolicy converts it to UTC.
    public Result<bool> UpdateDeadline(TimeOnly deadlineEastern)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        Settings = Settings with { TeamPickDeadlineEastern = deadlineEastern };
        return Result<bool>.Success(true);
    }

    public Result<bool> SaveLeagueRules(LeagueRules rules)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        Settings = Settings with { Rules = rules };
        return Result<bool>.Success(true);
    }

    // The fine and every pool's payout plan are saved together or not at all.
    public Result<bool> SaveFinesAndPayouts(Money fine, IReadOnlyCollection<PayoutPlan> plans)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        var validFine = PayoutRules.ValidateFine(fine);
        if (!validFine.IsSuccess) return Result<bool>.Failure(validFine.Error!.Code, validFine.Error.Message);
        var validPlans = PayoutRules.Validate(plans);
        if (!validPlans.IsSuccess) return Result<bool>.Failure(validPlans.Error!.Code, validPlans.Error.Message);
        MissedPickFine = fine;
        PayoutPlans = validPlans.Value!;
        return Result<bool>.Success(true);
    }

    public Result<bool> SaveDoubleDogWeeks(IReadOnlyCollection<int> weeks)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        var validated = DoubleDogWeekRules.Validate(weeks);
        if (!validated.IsSuccess) return Result<bool>.Failure(validated.Error!.Code, validated.Error.Message);
        DoubleDogWeekNumbers = validated.Value!;
        Settings = Settings with { DoubleDogWeeks = DoubleDogWeekNumbers.Count };
        return Result<bool>.Success(true);
    }

    // Saving a resume step records progress only; it never touches the season's configuration.
    public Result<bool> SaveSetupStep(SetupStep step)
    {
        if (State != SeasonState.Setup) return Result<bool>.Failure("season.locked", "Only a season in Setup can be edited.");
        if (!Enum.IsDefined(step)) return Result<bool>.Failure("season.step", "Unknown setup step.");
        SetupStep = step;
        return Result<bool>.Success(true);
    }

    private static bool IsValidAmount(Money money) => money.Amount > 0 && decimal.Round(money.Amount, 2) == money.Amount;

    private static Error? ValidateBasics(int year, string name) =>
        string.IsNullOrWhiteSpace(name) ? new Error("season.name", "Season name is required.")
        : year is < 2000 or > 2100 ? new Error("season.year", "Season year must be between 2000 and 2100.")
        : null;

    public Result<bool> Activate() => State != SeasonState.Setup || !DoubleDogWeekRules.Validate(DoubleDogWeekNumbers).IsSuccess || Settings.DoubleDogWeeks != DoubleDogWeekNumbers.Count || Pools.Sum(pool => pool.Stake.Amount) != Settings.BuyIn.Amount
        ? Result<bool>.Failure("season.invalid", "Season settings cannot activate.")
        : ActivateInternal();
    private Result<bool> ActivateInternal() { State = SeasonState.Live; Raise(new SeasonActivated(Id)); return Result<bool>.Success(true); }
    public Result<bool> Complete() => State != SeasonState.Live ? Result<bool>.Failure("season.invalid", "Season is not live.") : CompleteInternal();
    private Result<bool> CompleteInternal() { State = SeasonState.Completed; return Result<bool>.Success(true); }
    public Result<bool> UpdateSettings(SeasonSettings value) => State != SeasonState.Setup ? Result<bool>.Failure("season.locked", "Settings are structural.") : Update(value);
    private Result<bool> Update(SeasonSettings value) { Settings = DoubleDogWeekNumbers.Count > 0 ? value with { DoubleDogWeeks = DoubleDogWeekNumbers.Count } : value; return Result<bool>.Success(true); }
}

public sealed class Membership(MembershipId id, UserId userId, SeasonId seasonId, MembershipRole role) : AggregateRoot
{
    public MembershipId Id { get; } = id;
    public UserId UserId { get; } = userId;
    public SeasonId SeasonId { get; } = seasonId;
    public MembershipRole Role { get; } = role;
    public bool IsActive { get; private set; } = true;
    public void Invite() => Raise(new MembershipInvited(Id));
    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
public sealed record MembershipInvited(MembershipId MembershipId) : IDomainEvent;
public sealed record SeasonActivated(SeasonId SeasonId) : IDomainEvent;
