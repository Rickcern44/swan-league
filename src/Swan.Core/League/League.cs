using Swan.Core.Common;

namespace Swan.Core.League;

public enum SeasonState { Setup, Live, Completed }
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
    public IReadOnlyList<PoolDefinition> Pools { get; private set; } = pools.ToList();

    // A blank season carries only the PRD defaults (W2): $100 buy-in split Main 60 / Dog 20 / Regular Season 10 / Playoff 10, three double-dog weeks, Sunday 1:00 PM ET deadline.
    public static Result<Season> CreateBlank(SeasonId id, int year, string name, bool isSimulation)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result<Season>.Failure("season.name", "Season name is required.");
        if (year is < 2000 or > 2100) return Result<Season>.Failure("season.year", "Season year must be between 2000 and 2100.");
        var buyIn = new Money(100m, "USD");
        PoolDefinition[] pools = [new("Main", new Money(60m, "USD")), new("Dog", new Money(20m, "USD")), new("Regular Season", new Money(10m, "USD")), new("Playoff", new Money(10m, "USD"))];
        return Result<Season>.Success(new Season(id, new SeasonSettings(buyIn, 3, new TimeOnly(13, 0)), pools) { Year = year, Name = name.Trim(), IsSimulation = isSimulation });
    }

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
