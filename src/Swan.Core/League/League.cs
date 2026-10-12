using Swan.Core.Common;

namespace Swan.Core.League;

public enum SeasonState { Setup, Live, Completed }
public enum MembershipRole { Commissioner, Administrator, Coach }
public sealed record SeasonSettings(Money BuyIn, int DoubleDogWeeks);
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
    public Result<bool> Activate() => State != SeasonState.Setup || Settings.DoubleDogWeeks < 3 || Settings.DoubleDogWeeks > 4 || pools.Sum(pool => pool.Stake.Amount) != Settings.BuyIn.Amount
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
