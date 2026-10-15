using Swan.Core.Common;

namespace Swan.Core.Picks;

public sealed record TeamPick(TeamId TeamId, GameId GameId, DateTimeOffset LockTime, bool IsBye = false, int PointValue = 1)
{
    public static TeamPick ForGame(TeamId teamId, GameId gameId, DateTimeOffset kickoff, ITeamPickLockSettings settings, DateTimeOffset weekStartUtc, bool isBye = false) =>
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
    // Counts the changes that were saved. A client sends the revision it last read, so a save from an out-of-date screen is noticed instead of overwriting a newer one.
    public int Revision { get; private set; }
    // A coach's explicit lock: from then on only the commissioner can change the roster.
    public bool IsLocked { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    // Only a roster with exactly nine teams may make weekly picks (R1); nothing fills a short one.
    public bool IsComplete => teams.Count == 9;

    // A coach saves their whole roster at once: exactly nine different teams of the season, and only while the draft is open and the roster is not locked.
    public Result<bool> SaveDraft(IReadOnlyCollection<TeamId> chosen, IReadOnlySet<TeamId> seasonTeams, bool draftOpen)
    {
        if (IsLocked || !draftOpen) return Result<bool>.Failure("roster.locked", "The roster can no longer be changed by the coach.");
        if (chosen.Count != 9) return Result<bool>.Failure("roster.count", "Choose exactly nine teams.");
        if (chosen.Distinct().Count() != chosen.Count) return Result<bool>.Failure("roster.duplicate", "Each team can be chosen only once.");
        if (chosen.Any(team => !seasonTeams.Contains(team))) return Result<bool>.Failure("roster.team", "Choose only teams that are in this season.");
        if (teams.SetEquals(chosen)) return Result<bool>.Success(true);
        teams.Clear();
        teams.UnionWith(chosen);
        Revision++;
        return Result<bool>.Success(true);
    }

    public Result<bool> Lock(bool draftOpen, DateTimeOffset now)
    {
        if (IsLocked || !draftOpen) return Result<bool>.Failure("roster.locked", "The roster can no longer be changed by the coach.");
        if (teams.Count != 9) return Result<bool>.Failure("roster.count", "A roster needs exactly nine teams before it can be locked.");
        IsLocked = true;
        LockedAt = now;
        Revision++;
        return Result<bool>.Success(true);
    }

    // The commissioner's correction, allowed at any time (before or after the deadline, locked or not): swap one team for another, or add a team to a roster that is short.
    public Result<bool> CorrectByCommissioner(TeamId? remove, TeamId add, IReadOnlySet<TeamId> seasonTeams, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<bool>.Failure("roster.reason", "A correction needs a reason.");
        if (!seasonTeams.Contains(add)) return Result<bool>.Failure("roster.team", "Choose a team that is in this season.");
        if (remove is { } existing && !teams.Contains(existing)) return Result<bool>.Failure("roster.correction.invalid", "The team to replace is not on this roster.");
        if (remove is null && teams.Count >= 9) return Result<bool>.Failure("roster.count", "The roster is full; choose a team to replace.");
        if (teams.Contains(add)) return Result<bool>.Failure("roster.duplicate", "That team is already on this roster.");
        if (remove is { } gone) teams.Remove(gone);
        teams.Add(add);
        Revision++;
        return Result<bool>.Success(true);
    }

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

public sealed class Ballot(BallotId id, MembershipId membershipId, WeekId weekId, IEnumerable<TeamId>? rosterTeamIds = null) : AggregateRoot, IScoringBallot
{
    private Ballot() : this(default, default, default) { }
    private readonly HashSet<TeamId>? rosterTeams = rosterTeamIds?.ToHashSet();
    private readonly List<TeamPick> picks = [];
    private readonly List<DogPick> dogs = [];
    private readonly List<PickChange> changes = [];
    public bool IsComplete => picks.Count == 3;
    // Counts the saves that changed something. A client sends the revision it last read, so a save made from an out-of-date screen is noticed rather than silently overwriting a newer one.
    public int Revision { get; private set; }
    public BallotId Id { get; } = id;
    public MembershipId MembershipId { get; } = membershipId;
    public WeekId WeekId { get; } = weekId;
    public IReadOnlyCollection<TeamPick> Picks => picks.AsReadOnly();
    public IReadOnlyCollection<DogPick> Dogs => dogs.AsReadOnly();
    public IReadOnlyCollection<PickChange> Changes => changes.AsReadOnly();
    string IScoringBallot.ScoringReference => Id.Value.ToString();
    IReadOnlyCollection<ScoringTeamPick> IScoringBallot.ScoringPicks => picks.Select(pick => new ScoringTeamPick(pick.TeamId, pick.GameId, pick.PointValue)).ToList();
    IReadOnlyCollection<ScoringDogPick> IScoringBallot.ScoringDogs => dogs.Select(dog => new ScoringDogPick(dog.TeamId, dog.GameId)).ToList();
    public Result<bool> AddPick(TeamPick pick, DateTimeOffset now) { if (pick.IsBye || !LockPolicy.CanChange(pick.LockTime, now) || picks.Count == 3 || picks.Any(item => item.TeamId == pick.TeamId) || rosterTeams is not null && !rosterTeams.Contains(pick.TeamId)) return Result<bool>.Failure("pick.invalid", "Pick is invalid or locked."); picks.Add(pick); Raise(new PickSubmitted(Id)); return Result<bool>.Success(true); }
    public Result<bool> ChangePick(TeamId from, TeamPick replacement, DateTimeOffset now, string reason) { var pick = picks.FirstOrDefault(item => item.TeamId == from); if (pick is null || !LockPolicy.CanChange(pick.LockTime, now) || !LockPolicy.CanChange(replacement.LockTime, now) || string.IsNullOrWhiteSpace(reason)) return Result<bool>.Failure("pick.locked", "Pick cannot change."); picks.Remove(pick); picks.Add(replacement); changes.Add(new(now, reason)); Raise(new PickChanged(Id)); return Result<bool>.Success(true); }
    // Saves the coach's whole editable ballot at once and either applies all of it or none of it. A selection whose lock time has passed can be kept as it is but never removed,
    // replaced or added; everything else may be rearranged. Team picks are limited to roster teams (R1, R2) and dogs follow R7, R8 and R16.
    public Result<bool> Save(IReadOnlyCollection<TeamPick> newPicks, IReadOnlyCollection<DogPick> newDogs, bool doubleDog, IReadOnlySet<TeamId> roster, DateTimeOffset now)
    {
        var dogLimit = doubleDog ? 2 : 1;
        if (newPicks.Count > 3 || newPicks.Select(pick => pick.TeamId).Distinct().Count() != newPicks.Count || newPicks.Any(pick => pick.IsBye || !roster.Contains(pick.TeamId)))
            return Result<bool>.Failure("pick.invalid", "Pick three or fewer different teams from your roster.");
        if (newDogs.Count > dogLimit || newDogs.Select(dog => dog.TeamId).Distinct().Count() != newDogs.Count || newDogs.Select(dog => dog.GameId).Distinct().Count() != newDogs.Count)
            return Result<bool>.Failure("dog.invalid", doubleDog ? "Pick up to two dogs from two different teams and games." : "Pick at most one dog.");

        // Keep the stored selection when the same team and game is sent again, so its lock time and point value are the ones it was saved with.
        var finalPicks = newPicks.Select(pick => picks.FirstOrDefault(existing => existing.TeamId == pick.TeamId && existing.GameId == pick.GameId) ?? pick).ToList();
        var finalDogs = newDogs.Select(dog => dogs.FirstOrDefault(existing => existing.TeamId == dog.TeamId && existing.GameId == dog.GameId) ?? dog).ToList();
        if (picks.Any(existing => !LockPolicy.CanChange(existing.LockTime, now) && !finalPicks.Contains(existing)) || finalPicks.Any(pick => !picks.Contains(pick) && !LockPolicy.CanChange(pick.LockTime, now)))
            return Result<bool>.Failure("pick.locked", "A pick whose game has locked cannot be added, changed or removed.");
        if (dogs.Any(existing => !LockPolicy.CanChange(existing.LockTime, now) && !finalDogs.Contains(existing)) || finalDogs.Any(dog => !dogs.Contains(dog) && !LockPolicy.CanChange(dog.LockTime, now)))
            return Result<bool>.Failure("dog.locked", "A dog whose game has locked cannot be added, changed or removed.");

        var unchanged = picks.Count == finalPicks.Count && picks.All(finalPicks.Contains) && dogs.Count == finalDogs.Count && dogs.All(finalDogs.Contains);
        if (unchanged) return Result<bool>.Success(true);

        var hadSelections = picks.Count > 0 || dogs.Count > 0;
        picks.Clear();
        picks.AddRange(finalPicks);
        dogs.Clear();
        dogs.AddRange(finalDogs);
        Revision++;
        if (hadSelections)
        {
            changes.Add(new(now, "Coach revised picks"));
            Raise(new PickChanged(Id));
        }
        else Raise(new PickSubmitted(Id));
        return Result<bool>.Success(true);
    }
    public Result<bool> AddDog(DogPick dog, bool doubleDog, DateTimeOffset now) { var limit = doubleDog ? 2 : 1; if (!LockPolicy.CanChange(dog.LockTime, now) || dogs.Count == limit || dogs.Any(item => item.TeamId == dog.TeamId || item.GameId == dog.GameId)) return Result<bool>.Failure("dog.invalid", "Dog pick is invalid."); dogs.Add(dog); return Result<bool>.Success(true); }
}
public sealed record PickSubmitted(BallotId BallotId) : IDomainEvent;
public sealed record PickChanged(BallotId BallotId) : IDomainEvent;
