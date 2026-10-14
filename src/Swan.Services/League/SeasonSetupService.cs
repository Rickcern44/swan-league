using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Swan.Core.Common;
using Swan.Core;
using Swan.Core.Integrations;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.League;

public sealed record SeasonSummary(SeasonId Id, int Year, string Name, bool IsSimulation, SeasonState State, SeasonSettings Settings, IReadOnlyList<PoolDefinition> Pools, SetupStep SetupStep = SetupStep.Basics);

public sealed record SetupMoney(Money BuyIn, Money Main, Money Dog, Money RegularSeason, Money Playoff);

// TeamId is a plain Guid so it reaches JSON as a string rather than an id wrapper object.
public sealed record SetupTeam(Guid TeamId, string Name, string Division, int? FinishPosition, int? PointValue);

public sealed record SetupTeams(IReadOnlyList<SetupTeam> Teams);

public sealed record ScheduleGame(string ProviderGameId, Guid HomeTeamId, string HomeTeam, Guid AwayTeamId, string AwayTeam, DateTimeOffset Kickoff);

public sealed record ScheduleWeek(int Week, IReadOnlyList<ScheduleGame> Games);

// A schedule either previewed (not saved, carries the fingerprint to confirm) or already imported.
public sealed record SchedulePlan(bool Imported, string? Fingerprint, int Season, int GameCount, IReadOnlyList<ScheduleWeek> Weeks);

public interface ISeasonSetupService
{
    Task<Result<SeasonSummary>> CreateBlankAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> GetSetupAsync(CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupBasicsAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupMoneyAsync(UserId actor, SetupMoney money, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupDeadlineAsync(UserId actor, TimeOnly deadlineEastern, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupStepAsync(UserId actor, SetupStep step, CancellationToken cancellationToken = default);
    Task<Result<SetupTeams>> GetSetupTeamsAsync(CancellationToken cancellationToken = default);
    Task<Result<SetupTeams>> UpdateSetupTeamsAsync(UserId actor, IReadOnlyList<TeamFinish> finishes, CancellationToken cancellationToken = default);
    Task<Result<SchedulePlan>> GetSetupScheduleAsync(CancellationToken cancellationToken = default);
    Task<Result<SchedulePlan>> PreviewScheduleAsync(CancellationToken cancellationToken = default);
    Task<Result<SchedulePlan>> ImportScheduleAsync(UserId actor, string fingerprint, CancellationToken cancellationToken = default);
}

public sealed class SeasonSetupService(SwanDbContext database, IAuditService audit, IScheduleProvider scheduleProvider, IMemoryCache previews, IClock clock) : ISeasonSetupService
{
    // Only one Setup season may exist; Live and Completed seasons are never touched. Stakes and settings are PRD defaults, not caller input.
    public async Task<Result<SeasonSummary>> CreateBlankAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default)
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), year, name, isSimulation);
        if (!season.IsSuccess) return Result<SeasonSummary>.Failure(season.Error!.Code, season.Error.Message);
        if (await database.Seasons.AnyAsync(value => value.State == SeasonState.Setup, cancellationToken)) return Result<SeasonSummary>.Failure("season.setup.exists", "A season is already in Setup.");
        database.Seasons.Add(season.Value!);
        var summary = ToSummary(season.Value!);
        try
        {
            // AuditService saves the shared context, so the season and its audit entry commit together.
            await audit.AppendAsync(actor, "season.setup.created", summary.Id.Value.ToString(), before: null, after: summary, cancellationToken: cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_seasons_single_setup" })
        {
            // A concurrent create won the unique index; nothing from this attempt was saved.
            database.ChangeTracker.Clear();
            return Result<SeasonSummary>.Failure("season.setup.exists", "A season is already in Setup.");
        }

        return Result<SeasonSummary>.Success(summary);
    }

    public async Task<Result<SeasonSummary>> GetSetupAsync(CancellationToken cancellationToken = default) =>
        await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken) is { } setup
            ? Result<SeasonSummary>.Success(ToSummary(setup))
            : Result<SeasonSummary>.Failure("season.missing", "No season is in Setup.");

    public Task<Result<SeasonSummary>> UpdateSetupBasicsAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default) =>
        EditSetupAsync(actor, "season.setup.updated", season => season.UpdateBasics(year, name, isSimulation), ToSummary, summary => (summary.Year, summary.Name, summary.IsSimulation), cancellationToken);

    public Task<Result<SeasonSummary>> UpdateSetupMoneyAsync(UserId actor, SetupMoney money, CancellationToken cancellationToken = default) =>
        EditSetupAsync(actor, "season.setup.money.updated", season => season.UpdateMoney(money.BuyIn, money.Main, money.Dog, money.RegularSeason, money.Playoff), ToMoney, value => value, cancellationToken);

    public Task<Result<SeasonSummary>> UpdateSetupDeadlineAsync(UserId actor, TimeOnly deadlineEastern, CancellationToken cancellationToken = default) =>
        EditSetupAsync(actor, "season.setup.deadline.updated", season => season.UpdateDeadline(deadlineEastern), season => new DeadlineAudit(season.Settings.TeamPickDeadlineEastern), value => value, cancellationToken);

    public Task<Result<SeasonSummary>> UpdateSetupStepAsync(UserId actor, SetupStep step, CancellationToken cancellationToken = default) =>
        EditSetupAsync(actor, "season.setup.step.updated", season => season.SaveSetupStep(step), season => new StepAudit(season.SetupStep.ToString()), value => value, cancellationToken);

    public async Task<Result<SetupTeams>> GetSetupTeamsAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        return setup is null ? Result<SetupTeams>.Failure("season.missing", "No season is in Setup.") : Result<SetupTeams>.Success(await ListTeamsAsync(setup.Id, cancellationToken));
    }

    // The finishes are replaced as one complete set under the same row lock as every other Setup edit.
    public Task<Result<SetupTeams>> UpdateSetupTeamsAsync(UserId actor, IReadOnlyList<TeamFinish> finishes, CancellationToken cancellationToken = default) =>
        InSetupAsync(async season =>
        {
            var catalog = await database.Teams.AsNoTracking().ToListAsync(cancellationToken);
            var built = SetupTeamFinishes.Create(season.Id, catalog, finishes);
            if (!built.IsSuccess) return Result<SetupTeams>.Failure(built.Error!.Code, built.Error.Message);
            var before = await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == season.Id).ToListAsync(cancellationToken);
            var names = catalog.ToDictionary(team => team.Id, team => team.Name);
            Dictionary<string, int> Named(IEnumerable<SeasonTeam> teams) => teams.ToDictionary(team => names[team.TeamId], team => team.FinishPosition);
            var (beforeFinishes, afterFinishes) = (Named(before), Named(built.Value!));
            if (beforeFinishes.Count == afterFinishes.Count && beforeFinishes.All(entry => afterFinishes.TryGetValue(entry.Key, out var finish) && finish == entry.Value))
                return Result<SetupTeams>.Success(await ListTeamsAsync(season.Id, cancellationToken));
            // Deleted first and immediately: the unique division finish index would reject swapping finishes in place.
            await database.SeasonTeams.Where(team => team.SeasonId == season.Id).ExecuteDeleteAsync(cancellationToken);
            database.SeasonTeams.AddRange(built.Value!);
            await audit.AppendAsync(actor, "season.setup.teams.updated", season.Id.Value.ToString(), before: beforeFinishes.OrderBy(entry => entry.Key).ToDictionary(), after: afterFinishes.OrderBy(entry => entry.Key).ToDictionary(), cancellationToken: cancellationToken);
            return Result<SetupTeams>.Success(await ListTeamsAsync(season.Id, cancellationToken));
        }, cancellationToken);

    // What has been imported for the Setup season; reads only the local schedule, never the provider.
    public async Task<Result<SchedulePlan>> GetSetupScheduleAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        if (setup is null) return Result<SchedulePlan>.Failure("season.missing", "No season is in Setup.");
        var weeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == setup.Id).ToListAsync(cancellationToken);
        return Result<SchedulePlan>.Success(await ToPlanAsync(setup.Year, weeks, imported: weeks.Count > 0, fingerprint: null, cancellationToken));
    }

    // A previewed schedule is held in memory this long so confirming imports exactly what was shown without asking the provider again (which is rate limited).
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(15);

    private static string PreviewKey(SeasonId seasonId, string fingerprint) => $"schedule-preview:{seasonId.Value}:{fingerprint}";

    // Reads the provider and validates; nothing is saved to the database. An unpublished season is a valid empty preview.
    public async Task<Result<SchedulePlan>> PreviewScheduleAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        if (setup is null) return Result<SchedulePlan>.Failure("season.missing", "No season is in Setup.");
        if (await database.Weeks.AnyAsync(week => week.SeasonId == setup.Id, cancellationToken)) return Result<SchedulePlan>.Failure("schedule.exists", "This season already has an imported schedule.");
        var fetched = await scheduleProvider.FetchRegularSeasonAsync(setup.Year, cancellationToken);
        if (!fetched.IsSuccess) return Result<SchedulePlan>.Failure(fetched.Error!.Code, fetched.Error.Message);
        if (fetched.Value!.Games.Count == 0) return Result<SchedulePlan>.Success(new SchedulePlan(false, null, setup.Year, 0, []));
        var built = await BuildWeeksAsync(setup.Id, fetched.Value.Games, cancellationToken);
        if (!built.IsSuccess) return Result<SchedulePlan>.Failure(built.Error!.Code, built.Error.Message);
        var fingerprint = ScheduleImportPlan.Fingerprint(built.Value!);
        previews.Set(PreviewKey(setup.Id, fingerprint), fetched.Value, PreviewLifetime);
        return Result<SchedulePlan>.Success(await ToPlanAsync(setup.Year, built.Value!, imported: false, fingerprint, cancellationToken));
    }

    // Saves the previewed schedule as Week aggregates, atomically. It imports what the preview held and never contacts the provider.
    public async Task<Result<SchedulePlan>> ImportScheduleAsync(UserId actor, string fingerprint, CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        if (setup is null) return Result<SchedulePlan>.Failure("season.missing", "No season is in Setup.");
        if (!previews.TryGetValue(PreviewKey(setup.Id, fingerprint), out ProviderSchedule? fetched) || fetched is null)
            return Result<SchedulePlan>.Failure("schedule.changed", "That preview is no longer available; preview the schedule again.");
        try
        {
            return await InSetupAsync(async season =>
            {
                if (season.Id != setup.Id || season.Year != setup.Year) return Result<SchedulePlan>.Failure("schedule.changed", "The Setup season changed while the schedule was being read; preview it again.");
                if (await database.Weeks.AnyAsync(week => week.SeasonId == season.Id, cancellationToken)) return Result<SchedulePlan>.Failure("schedule.exists", "This season already has an imported schedule.");
                var built = await BuildWeeksAsync(season.Id, fetched.Games, cancellationToken);
                if (!built.IsSuccess) return Result<SchedulePlan>.Failure(built.Error!.Code, built.Error.Message);
                var actual = ScheduleImportPlan.Fingerprint(built.Value!);
                if (!string.Equals(actual, fingerprint, StringComparison.Ordinal)) return Result<SchedulePlan>.Failure("schedule.changed", "The previewed schedule no longer matches the current teams; preview it again.");
                database.Weeks.AddRange(built.Value!);
                // The audit entry is the provenance record: where the schedule came from and when it was read and imported. The raw response is not kept.
                await audit.AppendAsync(actor, "season.setup.schedule.imported", season.Id.Value.ToString(), before: null,
                    after: new { Provider = "BALLDONTLIE", Season = season.Year, FetchedAt = fetched.FetchedAt, ImportedAt = clock.UtcNow, Weeks = built.Value!.Count, Games = built.Value.Sum(week => week.Games.Count), Fingerprint = actual }, cancellationToken: cancellationToken);
                return Result<SchedulePlan>.Success(await ToPlanAsync(season.Year, built.Value!, imported: true, fingerprint: null, cancellationToken));
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A game from this provider is already stored (another season or a concurrent import); nothing from this attempt was saved.
            database.ChangeTracker.Clear();
            return Result<SchedulePlan>.Failure("schedule.exists", "Some of these provider games have already been imported.");
        }
    }

    private async Task<Result<IReadOnlyList<Week>>> BuildWeeksAsync(SeasonId seasonId, IReadOnlyCollection<ProviderGame> games, CancellationToken cancellationToken)
    {
        var catalog = await database.Teams.AsNoTracking().ToListAsync(cancellationToken);
        var seasonTeams = (await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == seasonId).Select(team => team.TeamId).ToListAsync(cancellationToken)).ToHashSet();
        return ScheduleImportPlan.Build(seasonId, games, catalog, seasonTeams);
    }

    private async Task<SchedulePlan> ToPlanAsync(int year, IReadOnlyList<Week> weeks, bool imported, string? fingerprint, CancellationToken cancellationToken)
    {
        var names = (await database.Teams.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(team => team.Id, team => team.Name);
        var plan = weeks.OrderBy(week => week.Number).Select(week => new ScheduleWeek(week.Number, week.Games.OrderBy(game => game.Kickoff.Value).ThenBy(game => game.ProviderGameId, StringComparer.Ordinal)
            .Select(game => new ScheduleGame(game.ProviderGameId ?? "", game.HomeTeamId.Value, names[game.HomeTeamId], game.AwayTeamId.Value, names[game.AwayTeamId], game.Kickoff.Value)).ToList())).ToList();
        return new SchedulePlan(imported, fingerprint, year, plan.Sum(week => week.Games.Count), plan);
    }

    private async Task<SetupTeams> ListTeamsAsync(SeasonId seasonId, CancellationToken cancellationToken)
    {
        var finishes = await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == seasonId).ToDictionaryAsync(team => team.TeamId, team => team.FinishPosition, cancellationToken);
        var catalog = await database.Teams.AsNoTracking().ToListAsync(cancellationToken);
        return new(catalog.OrderBy(team => team.Division, StringComparer.Ordinal).ThenBy(team => team.Name, StringComparer.Ordinal)
            .Select(team => finishes.TryGetValue(team.Id, out var finish) ? new SetupTeam(team.Id.Value, team.Name, team.Division, finish, SetupTeamFinishes.PointValueFor(finish)) : new SetupTeam(team.Id.Value, team.Name, team.Division, null, null)).ToList());
    }

    // Edits the one Setup season under a row lock; `audited` is what the audit entry records and `changeKey` decides whether anything changed.
    private Task<Result<SeasonSummary>> EditSetupAsync<TAudited, TKey>(UserId actor, string action, Func<Season, Result<bool>> edit, Func<Season, TAudited> audited, Func<TAudited, TKey> changeKey, CancellationToken cancellationToken)
        where TKey : IEquatable<TKey> =>
        InSetupAsync(async season =>
        {
            var before = audited(season);
            var edited = edit(season);
            if (!edited.IsSuccess) return Result<SeasonSummary>.Failure(edited.Error!.Code, edited.Error.Message);
            var after = audited(season);
            // Nothing changed: also what a retry sees after a commit whose acknowledgement was lost, so it must not append a second audit entry.
            if (changeKey(before).Equals(changeKey(after))) return Result<SeasonSummary>.Success(ToSummary(season));
            await audit.AppendAsync(actor, action, season.Id.Value.ToString(), before, after, cancellationToken: cancellationToken);
            return Result<SeasonSummary>.Success(ToSummary(season));
        }, cancellationToken);

    // Runs `work` on the one Setup season inside a transaction that holds its row lock, and commits only if `work` succeeds.
    private async Task<Result<T>> InSetupAsync<T>(Func<Season, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        // The app's retrying execution strategy only allows a user transaction inside ExecuteAsync, which may rerun the whole delegate.
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            // The row lock keeps a concurrent activation from committing between this state check and the save.
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var season = (await database.Seasons.FromSqlInterpolated($"""select * from league.seasons where "State" = {(int)SeasonState.Setup} for update""").ToListAsync(cancellationToken)).FirstOrDefault();
            if (season is null) return Result<T>.Failure("season.missing", "No season is in Setup.");
            var result = await work(season);
            if (result.IsSuccess) await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    // Pools are looked up by name: a Setup row that predates persisted pools has none, and the first money edit must still be able to set them.
    private static MoneyAudit ToMoney(Season season)
    {
        Money? Stake(string name) => season.Pools.FirstOrDefault(pool => pool.Name == name)?.Stake;
        return new(season.Settings.BuyIn, Stake("Main"), Stake("Dog"), Stake("Regular Season"), Stake("Playoff"));
    }

    private sealed record StepAudit(string SetupStep);

    private sealed record DeadlineAudit(TimeOnly? TeamPickDeadlineEastern);

    private sealed record MoneyAudit(Money BuyIn, Money? Main, Money? Dog, Money? RegularSeason, Money? Playoff);

    private static SeasonSummary ToSummary(Season season) => new(season.Id, season.Year, season.Name, season.IsSimulation, season.State, season.Settings, season.Pools, season.SetupStep);
}
