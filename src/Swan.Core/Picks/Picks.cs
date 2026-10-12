using Swan.Core.Common;
using Swan.Core.League;

namespace Swan.Core.Picks;

public sealed record TeamPick(TeamId TeamId, GameId GameId, DateTimeOffset LockTime, bool IsBye = false)
{
    public static TeamPick ForGame(TeamId teamId, GameId gameId, DateTimeOffset kickoff, SeasonSettings settings, DateTimeOffset weekStartUtc, bool isBye = false) =>
        new(teamId, gameId, LockPolicy.TeamPickLockTime(kickoff, settings, weekStartUtc), isBye);
}

public sealed record DogPick(TeamId TeamId, GameId GameId, DateTimeOffset LockTime)
{
    public static DogPick ForGame(TeamId teamId, GameId gameId, DateTimeOffset kickoff) => new(teamId, gameId, LockPolicy.DogPickLockTime(kickoff));
}
public sealed record PickChange(DateTimeOffset ChangedAt, string Reason);

public sealed class Roster(RosterId id, SeasonId seasonId, MembershipId membershipId) : AggregateRoot
{
    private readonly HashSet<TeamId> teams = [];
    public RosterId Id { get; } = id;
    public SeasonId SeasonId { get; } = seasonId;
    public MembershipId MembershipId { get; } = membershipId;
    public IReadOnlyCollection<TeamId> Teams => teams;
    public Result<bool> AddTeam(TeamId teamId, bool windowOpen) => !windowOpen || teams.Count == 9 ? Result<bool>.Failure("roster.locked", "Roster cannot change.") : (teams.Add(teamId) ? Result<bool>.Success(true) : Result<bool>.Failure("roster.duplicate", "Team is already selected."));
    public Result<bool> Close() => teams.Count == 9 ? Result<bool>.Success(true) : Result<bool>.Failure("roster.count", "Roster requires exactly nine teams.");
    public Result<bool> Correct(TeamId remove, TeamId add, bool windowOpen, string reason) => !windowOpen
        ? Result<bool>.Failure("roster.locked", "Roster cannot change.")
        : string.IsNullOrWhiteSpace(reason)
            ? Result<bool>.Failure("roster.reason", "Corrections require a reason.")
            : CorrectInternal(remove, add);
    private Result<bool> CorrectInternal(TeamId remove, TeamId add) => !teams.Remove(remove) || !teams.Add(add)
        ? Result<bool>.Failure("roster.correction.invalid", "The roster correction is invalid.")
        : Result<bool>.Success(true);
}

public sealed class Ballot(BallotId id, MembershipId membershipId, WeekId weekId, IEnumerable<TeamId>? rosterTeamIds = null) : AggregateRoot
{
    private Ballot() : this(default, default, default) { }
    private readonly HashSet<TeamId>? rosterTeams = rosterTeamIds?.ToHashSet();
    private readonly List<TeamPick> picks = [];
    private readonly List<DogPick> dogs = [];
    private readonly List<PickChange> changes = [];
    public bool IsComplete => picks.Count == 3;
    public BallotId Id { get; } = id;
    public MembershipId MembershipId { get; } = membershipId;
    public WeekId WeekId { get; } = weekId;
    public IReadOnlyCollection<TeamPick> Picks => picks.AsReadOnly();
    public IReadOnlyCollection<DogPick> Dogs => dogs.AsReadOnly();
    public IReadOnlyCollection<PickChange> Changes => changes.AsReadOnly();
    public Result<bool> AddPick(TeamPick pick, DateTimeOffset now) { if (pick.IsBye || !LockPolicy.CanChange(pick.LockTime, now) || picks.Count == 3 || picks.Any(item => item.TeamId == pick.TeamId) || rosterTeams is not null && !rosterTeams.Contains(pick.TeamId)) return Result<bool>.Failure("pick.invalid", "Pick is invalid or locked."); picks.Add(pick); Raise(new PickSubmitted(Id)); return Result<bool>.Success(true); }
    public Result<bool> ChangePick(TeamId from, TeamPick replacement, DateTimeOffset now, string reason) { var pick = picks.FirstOrDefault(item => item.TeamId == from); if (pick is null || !LockPolicy.CanChange(pick.LockTime, now) || !LockPolicy.CanChange(replacement.LockTime, now) || string.IsNullOrWhiteSpace(reason)) return Result<bool>.Failure("pick.locked", "Pick cannot change."); picks.Remove(pick); picks.Add(replacement); changes.Add(new(now, reason)); Raise(new PickChanged(Id)); return Result<bool>.Success(true); }
    public Result<bool> AddDog(DogPick dog, bool doubleDog, DateTimeOffset now) { var limit = doubleDog ? 2 : 1; if (!LockPolicy.CanChange(dog.LockTime, now) || dogs.Count == limit || dogs.Any(item => item.TeamId == dog.TeamId || item.GameId == dog.GameId)) return Result<bool>.Failure("dog.invalid", "Dog pick is invalid."); dogs.Add(dog); return Result<bool>.Success(true); }
}
public sealed record PickSubmitted(BallotId BallotId) : IDomainEvent;
public sealed record PickChanged(BallotId BallotId) : IDomainEvent;
