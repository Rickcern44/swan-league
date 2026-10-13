using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.League;

public sealed record SeasonSummary(SeasonId Id, int Year, string Name, bool IsSimulation, SeasonState State, SeasonSettings Settings, IReadOnlyList<PoolDefinition> Pools);

public sealed record SetupMoney(Money BuyIn, Money Main, Money Dog, Money RegularSeason, Money Playoff);

public interface ISeasonSetupService
{
    Task<Result<SeasonSummary>> CreateBlankAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> GetSetupAsync(CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupBasicsAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupMoneyAsync(UserId actor, SetupMoney money, CancellationToken cancellationToken = default);
}

public sealed class SeasonSetupService(SwanDbContext database, IAuditService audit) : ISeasonSetupService
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

    // Edits the one Setup season under a row lock; `audited` is what the audit entry records and `changeKey` decides whether anything changed.
    private async Task<Result<SeasonSummary>> EditSetupAsync<TAudited, TKey>(UserId actor, string action, Func<Season, Result<bool>> edit, Func<Season, TAudited> audited, Func<TAudited, TKey> changeKey, CancellationToken cancellationToken)
        where TKey : IEquatable<TKey>
    {
        // The app's retrying execution strategy only allows a user transaction inside ExecuteAsync, which may rerun the whole delegate.
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            // The row lock keeps a concurrent activation from committing between this state check and the save.
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var season = (await database.Seasons.FromSqlInterpolated($"""select * from league.seasons where "State" = {(int)SeasonState.Setup} for update""").ToListAsync(cancellationToken)).FirstOrDefault();
            if (season is null) return Result<SeasonSummary>.Failure("season.missing", "No season is in Setup.");
            var before = audited(season);
            var edited = edit(season);
            if (!edited.IsSuccess) return Result<SeasonSummary>.Failure(edited.Error!.Code, edited.Error.Message);
            var after = audited(season);
            // Nothing changed: also what a retry sees after a commit whose acknowledgement was lost, so it must not append a second audit entry.
            if (changeKey(before).Equals(changeKey(after))) return Result<SeasonSummary>.Success(ToSummary(season));
            await audit.AppendAsync(actor, action, season.Id.Value.ToString(), before, after, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<SeasonSummary>.Success(ToSummary(season));
        });
    }

    // Pools are looked up by name: a Setup row that predates persisted pools has none, and the first money edit must still be able to set them.
    private static MoneyAudit ToMoney(Season season)
    {
        Money? Stake(string name) => season.Pools.FirstOrDefault(pool => pool.Name == name)?.Stake;
        return new(season.Settings.BuyIn, Stake("Main"), Stake("Dog"), Stake("Regular Season"), Stake("Playoff"));
    }

    private sealed record MoneyAudit(Money BuyIn, Money? Main, Money? Dog, Money? RegularSeason, Money? Playoff);

    private static SeasonSummary ToSummary(Season season) => new(season.Id, season.Year, season.Name, season.IsSimulation, season.State, season.Settings, season.Pools);
}
