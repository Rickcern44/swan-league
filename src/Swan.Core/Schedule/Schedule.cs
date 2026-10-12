using Swan.Core.Common;

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
public sealed record DogOption(TeamId TeamId);

public sealed class Game(GameId id, TeamId homeTeamId, TeamId awayTeamId, Kickoff kickoff, bool excluded = false)
{
    private readonly List<Spread> lineHistory = [];
    public GameId Id { get; } = id;
    public TeamId HomeTeamId { get; } = homeTeamId;
    public TeamId AwayTeamId { get; } = awayTeamId;
    public Kickoff Kickoff { get; } = kickoff;
    public bool Excluded { get; } = excluded;
    public Spread? ApprovedLine { get; private set; }
    public GameResult? Result { get; private set; }
    public IReadOnlyCollection<Spread> LineHistory => lineHistory.AsReadOnly();
    internal Result<bool> AddLine(Spread line, bool frozen) { if (frozen) return Result<bool>.Failure("game.frozen", "Approved lines are frozen."); lineHistory.Add(line); return Result<bool>.Success(true); }
    internal Result<bool> ApproveLine(bool frozen) { if (frozen || lineHistory.Count == 0) return Result<bool>.Failure("game.line", "A line is required and must not be frozen."); ApprovedLine = lineHistory[^1]; return Result<bool>.Success(true); }
    internal Result<bool> ConfirmResult(GameResult result) { Result = result; return Result<bool>.Success(true); }
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
    public void SetDogOptions(IEnumerable<DogOption> options) { dogOptions.Clear(); dogOptions.AddRange(options); }
    public Result<bool> AddLine(GameId gameId, Spread line) => games.FirstOrDefault(game => game.Id == gameId)?.AddLine(line, State is not WeekState.Draft) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> ApproveLines() => State != WeekState.Draft || games.Any(game => !game.Excluded && game.ApprovedLine is null) ? Result<bool>.Failure("week.lines", "Every included game needs an approved line.") : Transition(WeekState.LinesApproved);
    public Result<bool> ApproveLatestLine(GameId gameId) => games.FirstOrDefault(game => game.Id == gameId)?.ApproveLine(State is not WeekState.Draft) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    public Result<bool> Publish() => State != WeekState.LinesApproved ? Result<bool>.Failure("week.state", "Lines must be approved.") : PublishInternal();
    public Result<bool> Lock() => State != WeekState.Open ? Result<bool>.Failure("week.state", "Week must be open.") : LockInternal();
    public Result<bool> FinalizeResults() => State != WeekState.Locked || games.Any(game => !game.Excluded && game.Result is null) ? Result<bool>.Failure("week.results", "Every included game needs a result.") : FinalizeInternal();
    public Result<bool> Reopen() => State is WeekState.Locked or WeekState.Final ? Transition(WeekState.Open) : Result<bool>.Failure("week.state", "Only locked or final weeks can reopen.");
    public Result<bool> ConfirmResult(GameId gameId, GameResult result) => games.FirstOrDefault(game => game.Id == gameId)?.ConfirmResult(result) ?? Result<bool>.Failure("game.missing", "Game does not exist.");
    private Result<bool> Transition(WeekState state) { State = state; return Result<bool>.Success(true); }
    private Result<bool> PublishInternal() { State = WeekState.Open; Raise(new WeekPublished(Id)); return Result<bool>.Success(true); }
    private Result<bool> LockInternal() { State = WeekState.Locked; Raise(new WeekLocked(Id)); return Result<bool>.Success(true); }
    private Result<bool> FinalizeInternal() { State = WeekState.Final; Raise(new ResultsConfirmed(Id)); return Result<bool>.Success(true); }
}
public sealed record WeekPublished(WeekId WeekId) : IDomainEvent;
public sealed record WeekLocked(WeekId WeekId) : IDomainEvent;
public sealed record ResultsConfirmed(WeekId WeekId) : IDomainEvent;
