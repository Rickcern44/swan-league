using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.League;

public sealed record SeasonSummary(SeasonId Id, int Year, string Name, bool IsSimulation, SeasonState State, SeasonSettings Settings, IReadOnlyList<PoolDefinition> Pools);

public interface ISeasonSetupService
{
    Task<Result<SeasonSummary>> CreateBlankAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> GetSetupAsync(CancellationToken cancellationToken = default);
    Task<Result<SeasonSummary>> UpdateSetupBasicsAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default);
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

    public async Task<Result<SeasonSummary>> UpdateSetupBasicsAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default)
    {
        // The app's retrying execution strategy only allows a user transaction inside ExecuteAsync, which may rerun the whole delegate.
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            // The row lock keeps a concurrent activation from committing between this state check and the save.
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var season = (await database.Seasons.FromSqlInterpolated($"""select * from league.seasons where "State" = {(int)SeasonState.Setup} for update""").ToListAsync(cancellationToken)).FirstOrDefault();
            if (season is null) return Result<SeasonSummary>.Failure("season.missing", "No season is in Setup.");
            var before = ToSummary(season);
            var updated = season.UpdateBasics(year, name, isSimulation);
            if (!updated.IsSuccess) return Result<SeasonSummary>.Failure(updated.Error!.Code, updated.Error.Message);
            var after = ToSummary(season);
            // Nothing changed: also what a retry sees after a commit whose acknowledgement was lost, so it must not append a second audit entry.
            if ((before.Year, before.Name, before.IsSimulation) == (after.Year, after.Name, after.IsSimulation)) return Result<SeasonSummary>.Success(after);
            await audit.AppendAsync(actor, "season.setup.updated", after.Id.Value.ToString(), before, after, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<SeasonSummary>.Success(after);
        });
    }

    private static SeasonSummary ToSummary(Season season) => new(season.Id, season.Year, season.Name, season.IsSimulation, season.State, season.Settings, season.Pools);
}
