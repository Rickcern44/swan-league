using System.Security.Cryptography;
using System.Text;
using Swan.Core.Common;
using Swan.Core.Integrations;

namespace Swan.Core.Schedule;

public enum WeekState { Draft, LinesApproved, Open, Locked, Final }
public enum WeekKind { Regular, Playoff, SuperBowl }
public sealed record WeekType(WeekKind Kind, string? Round, bool PicksRequired);
public sealed record Team(TeamId Id, string Name, string? LogoUrl, DateTimeOffset CreatedAt, string Division)
{
    public static Result<Team> Create(TeamId id, string name, string? logoUrl, DateTimeOffset createdAt, string division) => string.IsNullOrWhiteSpace(division)
        ? Result<Team>.Failure("team.division.required", "Teams require a division.")
        : Result<Team>.Success(new Team(id, name, logoUrl, createdAt, division));
}
public sealed record SeasonTeam(SeasonTeamId Id, SeasonId SeasonId, TeamId TeamId, string Division, int FinishPosition, int FinishTies)
{
    public static bool HasUniqueFinishes(IEnumerable<SeasonTeam> teams) => teams.GroupBy(team => (team.Division, team.FinishPosition)).All(group => group.Count() == 1);
}
public sealed record TeamFinish(TeamId TeamId, int Finish);

// A Setup season's team finishes are entered as one complete set: every known team once, and each division holding one team at each of 1st to 4th.
public static class SetupTeamFinishes
{
    // R3: a team's point value is its prior-season division finish.
    public static int PointValueFor(int finish) => PointValue.Create(finish).Value!.Value;

    public static Result<IReadOnlyList<SeasonTeam>> Create(SeasonId seasonId, IReadOnlyCollection<Team> teams, IReadOnlyCollection<TeamFinish> finishes)
    {
        if (finishes.Count != teams.Count || finishes.Select(entry => entry.TeamId).Distinct().Count() != finishes.Count || finishes.Any(entry => teams.All(team => team.Id != entry.TeamId)))
            return Failure($"Submit each of the {teams.Count} teams exactly once.");
        if (finishes.Any(entry => entry.Finish is < 1 or > 4)) return Failure("Every finish must be 1st, 2nd, 3rd or 4th.");
        var byTeam = finishes.ToDictionary(entry => entry.TeamId, entry => entry.Finish);
        foreach (var division in teams.GroupBy(team => team.Division).OrderBy(group => group.Key, StringComparer.Ordinal))
            if (!division.Select(team => byTeam[team.Id]).Order().SequenceEqual(Enumerable.Range(1, 4)))
                return Failure($"{division.Key} needs exactly one team at each of 1st, 2nd, 3rd and 4th.");
        IReadOnlyList<SeasonTeam> built = teams.Select(team => new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), seasonId, team.Id, team.Division, byTeam[team.Id], 0)).ToList();
        return Result<IReadOnlyList<SeasonTeam>>.Success(built);
    }

    private static Result<IReadOnlyList<SeasonTeam>> Failure(string message) => Result<IReadOnlyList<SeasonTeam>>.Failure("season.teams", message);
}

// Turns a provider's regular-season schedule into Week aggregates for a Setup season, or says why it cannot.
public static class ScheduleImportPlan
{
    public const int RegularSeasonWeeks = DoubleDogWeekRules.RegularSeasonWeeks;

    public static Result<IReadOnlyList<Week>> Build(SeasonId seasonId, IReadOnlyCollection<ProviderGame> games, IReadOnlyCollection<Team> catalog)
    {
        if (games.Count == 0) return Failure("schedule.empty", "The provider has no regular-season games for this season.");
        var byName = catalog.ToDictionary(team => team.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>();
        var weeks = new SortedDictionary<int, Week>();
        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.Id)) return Failure("schedule.invalid", "A provider game has no id.");
            if (!seen.Add(game.Id)) return Failure("schedule.invalid", $"Provider game {game.Id} appears more than once.");
            if (game.Week is < 1 or > RegularSeasonWeeks) return Failure("schedule.invalid", $"Provider game {game.Id} is in unexpected week {game.Week}; regular-season weeks are 1 to {RegularSeasonWeeks}.");
            if (game.Kickoff is not { } kickoff) return Failure("schedule.invalid", $"Provider game {game.Id} has an unreadable kickoff time.");
            if (!byName.TryGetValue(game.HomeTeam, out var home)) return Failure("schedule.invalid", $"Provider game {game.Id} names an unknown team: {game.HomeTeam}.");
            if (!byName.TryGetValue(game.AwayTeam, out var away)) return Failure("schedule.invalid", $"Provider game {game.Id} names an unknown team: {game.AwayTeam}.");
            if (home.Id == away.Id) return Failure("schedule.invalid", $"Provider game {game.Id} has the same home and away team.");
            var utc = Kickoff.Create(kickoff.ToUniversalTime());
            if (!weeks.TryGetValue(game.Week, out var week))
                weeks[game.Week] = week = new Week(new WeekId(Guid.NewGuid()), seasonId, game.Week, new WeekType(WeekKind.Regular, null, true));
            week.AddGame(new Game(new GameId(Guid.NewGuid()), home.Id, away.Id, utc.Value!, providerGameId: game.Id));
        }

        // A partial season would promise weeks that do not exist, so every regular-season week must be present.
        var missing = Enumerable.Range(1, RegularSeasonWeeks).Where(number => !weeks.ContainsKey(number)).ToList();
        if (missing.Count > 0) return Failure("schedule.invalid", $"The provider schedule has no games in {(missing.Count == 1 ? "week" : "weeks")} {string.Join(", ", missing)}; a complete regular season has weeks 1 to {RegularSeasonWeeks}.");
        return Result<IReadOnlyList<Week>>.Success(weeks.Values.ToList());
    }

    // Identifies exactly which games, matchups and kickoffs were previewed, so a confirmation can refuse a schedule that changed in between.
    public static string Fingerprint(IEnumerable<Week> weeks)
    {
        var lines = weeks.SelectMany(week => week.Games.Select(game => $"{game.ProviderGameId}|{week.Number}|{game.HomeTeamId.Value}|{game.AwayTeamId.Value}|{game.Kickoff.Value.UtcTicks}")).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static Result<IReadOnlyList<Week>> Failure(string code, string message) => Result<IReadOnlyList<Week>>.Failure(code, message);
}

public sealed record DogOption(TeamId TeamId);

public enum LineSource { Manual, Csv }

public sealed record AmericanMoneyline(int Home, int Away)
{
    public static Result<AmericanMoneyline> Create(int home, int away) => home == 0 || away == 0
        ? Result<AmericanMoneyline>.Failure("moneyline.invalid", "American odds cannot be zero.")
        : Result<AmericanMoneyline>.Success(new(home, away));
}

public sealed record GameLine(Spread Spread, int? HomeMoneyline, int? AwayMoneyline, LineSource Source, DateTimeOffset EnteredAt, UserId EnteredBy);

public enum ResultDraftSource { Provider, Manual, Override }

public sealed record ResultDraft(GameResult Result, ResultDraftSource Source, DateTimeOffset UpdatedAt, string? Reason);

public sealed class Game(GameId id, TeamId homeTeamId, TeamId awayTeamId, Kickoff kickoff, bool excluded = false, string? providerGameId = null)
{
    private readonly List<Spread> lineHistory = [];
    private readonly List<GameLine> lineDetails = [];
    public GameId Id { get; } = id;
    public TeamId HomeTeamId { get; } = homeTeamId;
    public TeamId AwayTeamId { get; } = awayTeamId;
    public Kickoff Kickoff { get; } = kickoff;
    public bool Excluded { get; private set; } = excluded;
    // Set only for games imported from a schedule provider; a source game can be imported once.
    public string? ProviderGameId { get; } = providerGameId;
    public Spread? ApprovedLine { get; private set; }
    public GameResult? Result { get; private set; }
    public ResultDraft? ProposedResult { get; private set; }
    public IReadOnlyCollection<Spread> LineHistory => lineHistory.AsReadOnly();
    public IReadOnlyCollection<GameLine> LineDetails => lineDetails.AsReadOnly();
    public GameLine? LatestLine => lineDetails.LastOrDefault();
    // A line was added after the one that is approved (or none is approved yet), so approving the week now would freeze an out-of-date spread.
    public bool HasUnapprovedLine => lineHistory.Count > 0 && lineHistory[^1] != ApprovedLine;
    internal Result<bool> AddLine(Spread line, bool frozen) => AddLine(line, null, LineSource.Manual, DateTimeOffset.UtcNow, default, frozen);
    internal Result<bool> AddLine(Spread line, AmericanMoneyline? moneyline, LineSource source, DateTimeOffset enteredAt, UserId enteredBy, bool frozen)
    {
        if (frozen) return Result<bool>.Failure("game.frozen", "Approved lines are frozen.");
        lineHistory.Add(line);
        lineDetails.Add(new GameLine(line, moneyline?.Home, moneyline?.Away, source, enteredAt, enteredBy));
        return Result<bool>.Success(true);
    }
    internal Result<bool> SetExcluded(bool excluded, WeekState state)
    {
        if (state is not WeekState.Draft and not WeekState.LinesApproved) return Result<bool>.Failure("week.state", "Games can only be included or excluded while lines are being prepared.");
        Excluded = excluded;
        return Result<bool>.Success(true);
    }
    internal Result<bool> ApproveLine(bool frozen) { if (frozen || lineHistory.Count == 0) return Result<bool>.Failure("game.line", "A line is required and must not be frozen."); ApprovedLine = lineHistory[^1]; return Result<bool>.Success(true); }
    internal Result<bool> ConfirmResult(GameResult result) { Result = result; return Result<bool>.Success(true); }
    internal Result<bool> ProposeResult(GameResult result, ResultDraftSource source, DateTimeOffset updatedAt, string? reason)
    {
        if (Result is not null) return Result<bool>.Failure("game.result.confirmed", "A confirmed result cannot be overwritten.");
        if (source == ResultDraftSource.Override && string.IsNullOrWhiteSpace(reason)) return Result<bool>.Failure("result.override.reason", "An override reason is required.");
        if (ProposedResult is { Source: ResultDraftSource.Manual or ResultDraftSource.Override } && source == ResultDraftSource.Provider) return Result<bool>.Success(true);
        ProposedResult = new ResultDraft(result, source, updatedAt, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        return Result<bool>.Success(true);
    }
    internal Result<bool> ConfirmProposedResult() => ProposedResult is null
        ? Result<bool>.Failure("game.result.missing", "The game has no proposed result.")
        : ConfirmResult(ProposedResult.Result);
}

public sealed class Week(WeekId id, SeasonId seasonId, int number, WeekType type) : AggregateRoot
{
    private readonly List<Game> games = [];
    private readonly List<DogOption> dogOptions = [];
    public WeekId Id { get; } = id;
    public SeasonId SeasonId { get; } = seasonId;
    public int Number { get; } = number;
    public WeekType Type { get; } = type;
    public WeekState State { get; private set; } = WeekState.Draft;
    public IReadOnlyCollection<Game> Games => games.AsReadOnly();
    public IReadOnlyCollection<DogOption> DogOptions => dogOptions.AsReadOnly();
    public void AddGame(Game game) => games.Add(game);
    private void SetDogOptions(IEnumerable<DogOption> options) { dogOptions.Clear(); dogOptions.AddRange(options); }
    // Spread is signed from the home team's perspective: +x makes the home team the underdog, -x the away team, 0 has none.
    public Result<bool> GenerateDogOptions()
    {
        if (State != WeekState.Draft) return Result<bool>.Failure("week.state", "Dog options can only change while the week is a draft.");
        var included = games.Where(game => !game.Excluded).ToList();
        if (included.Any(game => game.ApprovedLine is null)) return Result<bool>.Failure("week.lines", "Every included game needs an approved line before dog options are generated.");
        SetDogOptions(included.Where(game => game.ApprovedLine!.Value != 0).Select(game => new DogOption(game.ApprovedLine!.Value > 0 ? game.HomeTeamId : game.AwayTeamId)));
        return Result<bool>.Success(true);
    }
    public Result<bool> ReplaceDogOptions(IReadOnlyCollection<TeamId> teamIds)
    {
        if (State != WeekState.Draft) return Result<bool>.Failure("week.state", "Dog options can only change while the week is a draft.");
        var eligible = games.Where(game => !game.Excluded).SelectMany(game => new[] { game.HomeTeamId, game.AwayTeamId }).ToHashSet();
        if (teamIds.Distinct().Count() != teamIds.Count || teamIds.Any(teamId => !eligible.Contains(teamId))) return Result<bool>.Failure("week.dogs", "Dog options must be unique teams from this week's included games.");
        SetDogOptions(teamIds.Select(teamId => new DogOption(teamId)));
        return Result<bool>.Success(true);
    }
    public Result<bool> AddLine(GameId gameId, Spread line) => games.FirstOrDefault(game => game.Id == gameId)?.AddLine(line, State is not WeekState.Draft) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> AddLine(GameId gameId, Spread line, AmericanMoneyline? moneyline, LineSource source, DateTimeOffset enteredAt, UserId enteredBy) => games.FirstOrDefault(game => game.Id == gameId)?.AddLine(line, moneyline, source, enteredAt, enteredBy, State is not WeekState.Draft) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> SetGameExclusion(GameId gameId, bool excluded)
    {
        var game = games.FirstOrDefault(candidate => candidate.Id == gameId);
        var result = game?.SetExcluded(excluded, State) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
        if (result.IsSuccess && excluded && game is not null) dogOptions.RemoveAll(option => option.TeamId == game.HomeTeamId || option.TeamId == game.AwayTeamId);
        return result;
    }
    public Result<bool> ApproveLines() => State != WeekState.Draft || games.Any(game => !game.Excluded && (game.ApprovedLine is null || game.HasUnapprovedLine)) ? Result<bool>.Failure("week.lines", "Every included game needs its latest line approved.") : Transition(WeekState.LinesApproved);
    public Result<bool> ApproveLatestLine(GameId gameId) => games.FirstOrDefault(game => game.Id == gameId)?.ApproveLine(State is not WeekState.Draft) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> Publish() => State != WeekState.LinesApproved ? Result<bool>.Failure("week.state", "Lines must be approved.") : PublishInternal();
    public Result<bool> Lock() => State != WeekState.Open ? Result<bool>.Failure("week.state", "Week must be open.") : LockInternal();
    public Result<bool> FinalizeResults() => State != WeekState.Locked || games.Any(game => !game.Excluded && game.Result is null) ? Result<bool>.Failure("week.results", "Every included game needs a result.") : FinalizeInternal();
    public Result<bool> Reopen() => State is WeekState.Locked or WeekState.Final ? Transition(WeekState.Open) : Result<bool>.Failure("week.state", "Only locked or final weeks can reopen.");
    public Result<bool> ConfirmResult(GameId gameId, GameResult result) => State != WeekState.Locked ? Result<bool>.Failure("week.state", "Results can only be confirmed while the week is locked.") : games.FirstOrDefault(game => game.Id == gameId)?.ConfirmResult(result) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> ProposeResult(GameId gameId, GameResult result, ResultDraftSource source, DateTimeOffset updatedAt, string? reason) => State != WeekState.Locked
        ? Result<bool>.Failure("week.state", "Results can only be proposed while the week is locked.")
        : games.FirstOrDefault(game => game.Id == gameId)?.ProposeResult(result, source, updatedAt, reason) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> ConfirmProposedResults()
    {
        if (State != WeekState.Locked) return Result<bool>.Failure("week.state", "Results can only be confirmed while the week is locked.");
        foreach (var game in games.Where(game => !game.Excluded && game.Result is null))
        {
            var confirmed = game.ConfirmProposedResult();
            if (!confirmed.IsSuccess) return confirmed;
        }
        return Result<bool>.Success(true);
    }
    private Result<bool> Transition(WeekState state) { State = state; return Result<bool>.Success(true); }
    private Result<bool> PublishInternal() { State = WeekState.Open; Raise(new WeekPublished(Id)); return Result<bool>.Success(true); }
    private Result<bool> LockInternal() { State = WeekState.Locked; Raise(new WeekLocked(Id)); return Result<bool>.Success(true); }
    private Result<bool> FinalizeInternal() { State = WeekState.Final; Raise(new ResultsConfirmed(Id)); return Result<bool>.Success(true); }
}
public sealed record WeekPublished(WeekId WeekId) : IDomainEvent;
public sealed record WeekLocked(WeekId WeekId) : IDomainEvent;
public sealed record ResultsConfirmed(WeekId WeekId) : IDomainEvent;
