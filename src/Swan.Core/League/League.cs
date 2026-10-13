using Swan.Core.Common;

namespace Swan.Core.League;

public enum SeasonState { Setup, Live, Completed }
// Where the Setup wizard resumes; ScheduleImport is the first step that is not built yet, so it means every implemented step is done.
public enum SetupStep { Basics, Money, WeeklyDeadline, TeamsAndPoints, ScheduleImport }
public enum MembershipRole { Commissioner, Administrator, Coach }
public sealed record SeasonSettings(Money BuyIn, int DoubleDogWeeks, TimeOnly? TeamPickDeadlineEastern = null) : ITeamPickLockSettings;
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
    public IReadOnlyList<PoolDefinition> Pools { get; private set; } = pools.ToList();

    // A blank season carries only the PRD defaults (W2): $100 buy-in split Main 60 / Dog 20 / Regular Season 10 / Playoff 10, three double-dog weeks, Sunday 1:00 PM ET deadline.
    public static Result<Season> CreateBlank(SeasonId id, int year, string name, bool isSimulation)
    {
        if (ValidateBasics(year, name) is { } invalid) return Result<Season>.Failure(invalid.Code, invalid.Message);
        var buyIn = new Money(100m, "USD");
        PoolDefinition[] pools = [new("Main", new Money(60m, "USD")), new("Dog", new Money(20m, "USD")), new("Regular Season", new Money(10m, "USD")), new("Playoff", new Money(10m, "USD"))];
        return Result<Season>.Success(new Season(id, new SeasonSettings(buyIn, 3, new TimeOnly(13, 0)), pools) { Year = year, Name = name.Trim(), IsSimulation = isSimulation });
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

    public Result<bool> Activate() => State != SeasonState.Setup || Settings.DoubleDogWeeks < 3 || Settings.DoubleDogWeeks > 4 || Pools.Sum(pool => pool.Stake.Amount) != Settings.BuyIn.Amount
        ? Result<bool>.Failure("season.invalid", "Season settings cannot activate.")
        : ActivateInternal();
    private Result<bool> ActivateInternal() { State = SeasonState.Live; Raise(new SeasonActivated(Id)); return Result<bool>.Success(true); }
    public Result<bool> Complete() => State != SeasonState.Live ? Result<bool>.Failure("season.invalid", "Season is not live.") : CompleteInternal();
    private Result<bool> CompleteInternal() { State = SeasonState.Completed; return Result<bool>.Success(true); }
    public Result<bool> UpdateSettings(SeasonSettings value) => State != SeasonState.Setup ? Result<bool>.Failure("season.locked", "Settings are structural.") : Update(value);
    private Result<bool> Update(SeasonSettings value) { Settings = value; return Result<bool>.Success(true); }
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
}
public sealed record MembershipInvited(MembershipId MembershipId) : IDomainEvent;
public sealed record SeasonActivated(SeasonId SeasonId) : IDomainEvent;
