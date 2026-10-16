using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Development;

public sealed record DevelopmentResetSummary(IReadOnlyDictionary<string, int> Removed);

// A local-development convenience: clears everything that depends on a season so the app starts over at "no Setup season". It keeps the team catalog, the people (users, login links) and
// their sessions, and the background-job schedule. Membership-owned notification settings and push registrations go with the memberships. Everything happens in one transaction, children before parents, so it either clears it all or none of it. The route that reaches this
// is mapped only in Development; this class does not decide that.
public sealed class DevelopmentResetService(SwanDbContext database, IAuditService audit)
{
    // Children first. The names are what the summary reports.
    private static readonly (string Name, string Table)[] Tables =
    [
        ("teamPicks", "picks.team_picks"), ("dogPicks", "picks.dog_picks"), ("pickChanges", "picks.pick_changes"), ("ballots", "picks.ballots"), ("rosters", "picks.rosters"),
        ("scoreLines", "scoring.score_lines"), ("weekScores", "scoring.week_scores"),
        ("ledgerItems", "money.ledger_items"), ("ledgers", "money.ledgers"), ("poolPlacements", "money.pool_placements"), ("poolSettlements", "money.pool_settlements"),
        ("dogOptions", "schedule.dog_options"), ("games", "schedule.games"), ("weeks", "schedule.weeks"), ("seasonTeams", "schedule.season_teams"),
        ("notificationPreferences", "communication.notification_preferences"), ("pushSubscriptions", "communication.push_subscriptions"),
        ("memberships", "league.memberships"), ("seasons", "league.seasons"),
        ("notifications", "communication.notifications"), ("announcements", "communication.announcements"),
        ("auditEntries", "platform.audit_entries"),
    ];

    public Task<Result<DevelopmentResetSummary>> ResetSeasonDataAsync(UserId actor, CancellationToken cancellationToken = default) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var removed = new Dictionary<string, int>();
            // The table names are the constants above, never input.
#pragma warning disable EF1002
            foreach (var (name, table) in Tables)
                removed[name] = await database.Database.ExecuteSqlRawAsync($"delete from {table}", cancellationToken);
#pragma warning restore EF1002
            // The history is cleared, then the reset itself is recorded as its first entry.
            await audit.AppendAsync(actor, "development.season-data-reset", "development", before: null, after: new { Removed = removed }, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<DevelopmentResetSummary>.Success(new DevelopmentResetSummary(removed));
        });
}
