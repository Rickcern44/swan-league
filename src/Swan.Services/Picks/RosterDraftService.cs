using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Picks;

public sealed record DraftTeam(Guid TeamId, string Name, string Division, int PointValue, bool Selected);

// State is "no-membership" (no active coach membership in a live season), "unopened" (the commissioner has not opened the draft), "open" (the coach may still choose), "locked" (the coach locked it before the deadline) or "closed" (the deadline has passed).
public sealed record RosterDesk(string State, Guid? SeasonId, string? SeasonName, DateTimeOffset? Deadline, string? DeadlineEastern, DateTimeOffset ServerTime, bool IsLocked, DateTimeOffset? LockedAt, bool Complete, int Revision, IReadOnlyList<DraftTeam> Teams);

public sealed record SaveRoster(Guid SeasonId, int ExpectedRevision, IReadOnlyList<Guid> TeamIds);
public sealed record LockRoster(Guid SeasonId, int ExpectedRevision);

public interface ICoachRosterService
{
    Task<Result<RosterDesk>> GetAsync(UserId coach, CancellationToken cancellationToken = default);
    Task<Result<RosterDesk>> SaveAsync(UserId coach, SaveRoster request, CancellationToken cancellationToken = default);
    Task<Result<RosterDesk>> LockAsync(UserId coach, LockRoster request, CancellationToken cancellationToken = default);
}

// A coach drafts only their own roster of the newest live season they belong to; server time decides whether the draft is open.
public sealed class CoachRosterService(SwanDbContext database, IClock clock, IAuditService audit) : ICoachRosterService
{
    public async Task<Result<RosterDesk>> GetAsync(UserId coach, CancellationToken cancellationToken = default) =>
        Result<RosterDesk>.Success(await BuildAsync(await ResolveAsync(coach, cancellationToken), cancellationToken));

    public Task<Result<RosterDesk>> SaveAsync(UserId coach, SaveRoster request, CancellationToken cancellationToken = default) =>
        MutateAsync(coach, request.SeasonId, request.ExpectedRevision, "roster.saved", (roster, context, seasonTeams) =>
            roster.SaveDraft(request.TeamIds.Select(id => new TeamId(id)).ToList(), seasonTeams, context.Season!.RosterDraftOpen(clock.UtcNow)), cancellationToken);

    public Task<Result<RosterDesk>> LockAsync(UserId coach, LockRoster request, CancellationToken cancellationToken = default) =>
        MutateAsync(coach, request.SeasonId, request.ExpectedRevision, "roster.locked", (roster, context, _) =>
            roster.Lock(context.Season!.RosterDraftOpen(clock.UtcNow), clock.UtcNow), cancellationToken);

    private async Task<Result<RosterDesk>> MutateAsync(UserId coach, Guid seasonId, int expectedRevision, string action, Func<Roster, Context, IReadOnlySet<TeamId>, Result<bool>> change, CancellationToken cancellationToken)
    {
        // The retrying execution strategy only allows a user transaction inside ExecuteAsync, which may rerun the delegate.
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var first = await ResolveAsync(coach, cancellationToken);
            if (first.Membership is null) return Result<RosterDesk>.Failure("roster.unavailable", "You are not a coach in a live season.");
            // The row lock makes two requests by the same coach (and a commissioner's correction) take turns, so the revision check below is reliable.
            await database.Memberships.FromSqlInterpolated($"""select * from league.memberships where "Id" = {first.Membership.Id.Value} for update""").ToListAsync(cancellationToken);
            database.ChangeTracker.Clear();
            var context = await ResolveAsync(coach, cancellationToken);
            if (context.Membership is null || context.Season is null) return Result<RosterDesk>.Failure("roster.unavailable", "You are not a coach in a live season.");
            if (context.Season.Id.Value != seasonId) return Result<RosterDesk>.Failure("season.stale", "That is no longer your current season. Reload to see it.");
            var roster = context.Roster ?? new Roster(new RosterId(Guid.NewGuid()), context.Season.Id, context.Membership.Id);
            if (roster.Revision != expectedRevision) return Result<RosterDesk>.Failure("roster.stale", "Your roster changed since this page loaded. Reload to see the saved one.");

            var seasonTeams = (await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == context.Season.Id).Select(team => team.TeamId).ToListAsync(cancellationToken)).ToHashSet();
            var before = Summarize(roster);
            var revisionBefore = roster.Revision;
            var changed = change(roster, context, seasonTeams);
            if (!changed.IsSuccess)
            {
                database.ChangeTracker.Clear();
                return Result<RosterDesk>.Failure(changed.Error!.Code, changed.Error.Message);
            }

            if (context.Roster is null) database.Rosters.Add(roster);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                if (revisionBefore != roster.Revision) await audit.AppendAsync(coach, action, roster.Id.Value.ToString(), new { Roster = before }, new { Roster = Summarize(roster), roster.IsLocked }, cancellationToken: cancellationToken);
            }
            catch (DbUpdateException) when (context.Roster is null)
            {
                // Only a concurrent first save can collide on the one-roster-per-coach-and-season index.
                database.ChangeTracker.Clear();
                return Result<RosterDesk>.Failure("roster.stale", "Your roster changed since this page loaded. Reload to see the saved one.");
            }

            var desk = await BuildAsync(await ResolveAsync(coach, cancellationToken), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<RosterDesk>.Success(desk);
        });
    }

    private sealed record Context(Season? Season, Membership? Membership, Roster? Roster);

    private async Task<Context> ResolveAsync(UserId coach, CancellationToken cancellationToken)
    {
        var live = (await database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Live).ToListAsync(cancellationToken))
            .OrderByDescending(season => season.Year).ThenBy(season => season.Name, StringComparer.Ordinal).ThenBy(season => season.Id.Value).ToList();
        var seasonIds = live.Select(season => season.Id).ToList();
        var memberships = await database.Memberships.Where(item => item.UserId == coach && item.Role == MembershipRole.Coach && item.IsActive && seasonIds.Contains(item.SeasonId)).ToListAsync(cancellationToken);
        var membership = live.Select(season => memberships.FirstOrDefault(item => item.SeasonId == season.Id)).FirstOrDefault(item => item is not null);
        if (membership is null) return new(null, null, null);
        var season = live.First(item => item.Id == membership.SeasonId);
        var roster = await database.Rosters.FirstOrDefaultAsync(item => item.MembershipId == membership.Id && item.SeasonId == season.Id, cancellationToken);
        return new(season, membership, roster);
    }

    private async Task<RosterDesk> BuildAsync(Context context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (context.Season is null) return new("no-membership", null, null, null, null, now, false, null, false, 0, []);
        var season = context.Season;
        var chosen = context.Roster?.Teams.ToHashSet() ?? [];
        var teams = await SeasonCatalog.ListAsync(database, season.Id, cancellationToken);
        var open = season.RosterDraftOpen(now);
        var locked = context.Roster?.IsLocked ?? false;
        var state = season.RosterDraftDeadline is null ? "unopened" : !open ? "closed" : locked ? "locked" : "open";
        return new(state, season.Id.Value, season.Name, season.RosterDraftDeadline, season.RosterDraftDeadline is { } deadline ? EasternTime.ToLocalText(deadline) : null, now, locked, context.Roster?.LockedAt,
            context.Roster?.IsComplete ?? false, context.Roster?.Revision ?? 0,
            teams.Select(team => new DraftTeam(team.TeamId.Value, team.Name, team.Division, team.PointValue, chosen.Contains(team.TeamId))).ToList());
    }

    internal static string Summarize(Roster roster) => string.Join(",", roster.Teams.Select(team => team.Value).Order());
}

internal sealed record CatalogTeam(TeamId TeamId, string Name, string Division, int PointValue);

internal static class SeasonCatalog
{
    // The teams a roster may hold are the season's own teams; the point value is the division finish (R3).
    public static async Task<IReadOnlyList<CatalogTeam>> ListAsync(SwanDbContext database, SeasonId seasonId, CancellationToken cancellationToken)
    {
        var seasonTeams = await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == seasonId).ToListAsync(cancellationToken);
        var ids = seasonTeams.Select(team => team.TeamId).ToList();
        var names = await database.Teams.AsNoTracking().Where(team => ids.Contains(team.Id)).ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        return seasonTeams.Select(team => new CatalogTeam(team.TeamId, names.GetValueOrDefault(team.TeamId) ?? "Unknown team", team.Division, team.FinishPosition))
            .OrderBy(team => team.Division, StringComparer.Ordinal).ThenBy(team => team.PointValue).ThenBy(team => team.Name, StringComparer.Ordinal).ToList();
    }
}

public sealed record CommissionerCoach(Guid MembershipId, string Name, string Email, string Status, int TeamCount, IReadOnlyList<Guid> TeamIds, IReadOnlyList<string> Teams, bool IsLocked, DateTimeOffset? LockedAt, int Revision);
public sealed record CatalogEntry(Guid TeamId, string Name, string Division);
public sealed record CommissionerDraft(Guid SeasonId, string SeasonName, DateTimeOffset? Deadline, string? DeadlineEastern, bool DraftOpen, DateTimeOffset ServerTime, IReadOnlyList<CatalogEntry> Teams, IReadOnlyList<CommissionerCoach> Coaches, int PicksRemoved = 0);
public sealed record CorrectRoster(Guid SeasonId, Guid MembershipId, int ExpectedRevision, Guid? RemoveTeamId, Guid AddTeamId, string Reason);
public sealed record StartRosterDraft(Guid SeasonId, DateTime RosterDraftDeadlineEastern);

public interface ICommissionerRosterService
{
    Task<Result<CommissionerDraft>> GetAsync(UserId actor, CancellationToken cancellationToken = default);
    Task<Result<CommissionerDraft>> StartAsync(UserId actor, StartRosterDraft request, CancellationToken cancellationToken = default);
    Task<Result<CommissionerDraft>> CorrectAsync(UserId actor, CorrectRoster request, CancellationToken cancellationToken = default);
}

// The commissioner of the newest live season sees every coach's roster and alone may correct one. Authority is an active Commissioner membership of that season, never the global Admin role.
public sealed class CommissionerRosterService(SwanDbContext database, IClock clock, IAuditService audit) : ICommissionerRosterService
{
    public async Task<Result<CommissionerDraft>> GetAsync(UserId actor, CancellationToken cancellationToken = default)
    {
        var season = await CommissionedSeasonAsync(actor, cancellationToken);
        return season.IsSuccess ? Result<CommissionerDraft>.Success(await BuildAsync(season.Value!, cancellationToken)) : Result<CommissionerDraft>.Failure(season.Error!.Code, season.Error.Message);
    }

    public async Task<Result<CommissionerDraft>> StartAsync(UserId actor, StartRosterDraft request, CancellationToken cancellationToken = default)
    {
        var deadline = EasternTime.ToInstant(request.RosterDraftDeadlineEastern);
        if (!deadline.IsSuccess) return Result<CommissionerDraft>.Failure(deadline.Error!.Code, deadline.Error.Message);
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var found = await CommissionedSeasonAsync(actor, cancellationToken);
            if (!found.IsSuccess) return Result<CommissionerDraft>.Failure(found.Error!.Code, found.Error.Message);
            if (found.Value!.Id.Value != request.SeasonId) return Result<CommissionerDraft>.Failure("season.stale", "That is no longer the current season. Reload to see it.");
            var locked = await database.Seasons.FromSqlInterpolated($"""select * from league.seasons where "Id" = {request.SeasonId} and "State" = {(int)SeasonState.Live} for update""").ToListAsync(cancellationToken);
            if (locked.Count == 0) return Result<CommissionerDraft>.Failure("season.stale", "That is no longer the current season. Reload to see it.");
            var season = locked[0];
            var opened = season.OpenRosterDraft(deadline.Value, clock.UtcNow, hasActiveCommissioner: true);
            if (!opened.IsSuccess) return Result<CommissionerDraft>.Failure(opened.Error!.Code, opened.Error.Message);
            await database.SaveChangesAsync(cancellationToken);
            await audit.AppendAsync(actor, "roster-draft.started", season.Id.Value.ToString(), before: new { DraftOpen = false, RosterDraftDeadlineUtc = (DateTimeOffset?)null }, after: new { DraftOpen = true, RosterDraftDeadlineUtc = deadline.Value, RosterDraftDeadlineEastern = EasternTime.ToLocalText(deadline.Value), Commissioner = actor.Value }, cancellationToken: cancellationToken);
            var draft = await BuildAsync(season, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<CommissionerDraft>.Success(draft);
        });
    }

    public async Task<Result<CommissionerDraft>> CorrectAsync(UserId actor, CorrectRoster request, CancellationToken cancellationToken = default)
    {
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var found = await CommissionedSeasonAsync(actor, cancellationToken);
            if (!found.IsSuccess) return Result<CommissionerDraft>.Failure(found.Error!.Code, found.Error.Message);
            var season = found.Value!;
            if (season.Id.Value != request.SeasonId) return Result<CommissionerDraft>.Failure("season.stale", "That is no longer the current season. Reload to see it.");
            var membership = await database.Memberships.FirstOrDefaultAsync(item => item.Id == new MembershipId(request.MembershipId) && item.SeasonId == season.Id && item.Role == MembershipRole.Coach && item.IsActive, cancellationToken);
            if (membership is null) return Result<CommissionerDraft>.Failure("coach.missing", "That coach is not an active coach of this season.");
            // Takes turns with the coach's own saves.
            await database.Memberships.FromSqlInterpolated($"""select * from league.memberships where "Id" = {membership.Id.Value} for update""").ToListAsync(cancellationToken);
            database.ChangeTracker.Clear();
            var existing = await database.Rosters.FirstOrDefaultAsync(item => item.MembershipId == membership.Id && item.SeasonId == season.Id, cancellationToken);
            var roster = existing ?? new Roster(new RosterId(Guid.NewGuid()), season.Id, membership.Id);
            if (roster.Revision != request.ExpectedRevision) return Result<CommissionerDraft>.Failure("roster.stale", "That roster changed since this page loaded. Reload to see the saved one.");

            var seasonTeams = (await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == season.Id).Select(team => team.TeamId).ToListAsync(cancellationToken)).ToHashSet();
            var before = CoachRosterService.Summarize(roster);
            var corrected = roster.CorrectByCommissioner(request.RemoveTeamId is { } remove ? new TeamId(remove) : null, new TeamId(request.AddTeamId), seasonTeams, request.Reason?.Trim() ?? "");
            if (!corrected.IsSuccess)
            {
                database.ChangeTracker.Clear();
                return Result<CommissionerDraft>.Failure(corrected.Error!.Code, corrected.Error.Message);
            }

            if (existing is null) database.Rosters.Add(roster);
            // A team taken off the roster can no longer be one of this coach's starts, so its picks come off the ballots of weeks that are not finished (a finished week's scores stand).
            var dropped = new List<(Ballot Ballot, string Before, int Removed)>();
            if (request.RemoveTeamId is { } removedTeamId)
            {
                var unfinishedWeeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == season.Id && (week.State == WeekState.Open || week.State == WeekState.Locked)).Select(week => week.Id).ToListAsync(cancellationToken);
                var ballots = await database.Ballots.Where(ballot => ballot.MembershipId == membership.Id && unfinishedWeeks.Contains(ballot.WeekId)).ToListAsync(cancellationToken);
                foreach (var ballot in ballots)
                {
                    var picksBefore = string.Join(",", ballot.Picks.Select(pick => pick.TeamId.Value).Order());
                    var removed = ballot.RemovePicksFor(new TeamId(removedTeamId), clock.UtcNow, $"Commissioner correction: {request.Reason!.Trim()}");
                    if (removed > 0) dropped.Add((ballot, picksBefore, removed));
                }
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);
                await audit.AppendAsync(actor, "roster.corrected", roster.Id.Value.ToString(),
                    new { Coach = membership.Id.Value, Roster = before, roster.IsLocked },
                    new { Coach = membership.Id.Value, Roster = CoachRosterService.Summarize(roster), roster.IsLocked },
                    request.Reason!.Trim(), cancellationToken);
                foreach (var (ballot, picksBefore, _) in dropped)
                    await audit.AppendAsync(actor, "ballot.pick-removed", ballot.Id.Value.ToString(),
                        new { Coach = membership.Id.Value, Week = ballot.WeekId.Value, Picks = picksBefore },
                        new { Coach = membership.Id.Value, Week = ballot.WeekId.Value, Picks = string.Join(",", ballot.Picks.Select(pick => pick.TeamId.Value).Order()) },
                        request.Reason!.Trim(), cancellationToken);
            }
            catch (DbUpdateException) when (existing is null)
            {
                database.ChangeTracker.Clear();
                return Result<CommissionerDraft>.Failure("roster.stale", "That roster changed since this page loaded. Reload to see the saved one.");
            }

            var draft = await BuildAsync(season, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<CommissionerDraft>.Success(draft with { PicksRemoved = dropped.Sum(item => item.Removed) });
        });
    }

    private async Task<Result<Season>> CommissionedSeasonAsync(UserId actor, CancellationToken cancellationToken)
    {
        var live = (await database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Live).ToListAsync(cancellationToken))
            .OrderByDescending(season => season.Year).ThenBy(season => season.Name, StringComparer.Ordinal).ThenBy(season => season.Id.Value).ToList();
        if (live.Count == 0) return Result<Season>.Failure("season.missing", "No season is live.");
        var current = live[0];
        var commissioner = await database.Memberships.AnyAsync(item => item.UserId == actor && item.SeasonId == current.Id && item.Role == MembershipRole.Commissioner && item.IsActive, cancellationToken);
        return commissioner ? Result<Season>.Success(current) : Result<Season>.Failure("commissioner.required", "Only the season's commissioner can manage its rosters.");
    }

    private async Task<CommissionerDraft> BuildAsync(Season season, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var catalog = await SeasonCatalog.ListAsync(database, season.Id, cancellationToken);
        var names = catalog.ToDictionary(team => team.TeamId, team => team.Name);
        var memberships = await database.Memberships.AsNoTracking().Where(item => item.SeasonId == season.Id && item.Role == MembershipRole.Coach && item.IsActive).ToListAsync(cancellationToken);
        var userIds = memberships.Select(item => item.UserId).ToList();
        var users = await database.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);
        var rosters = (await database.Rosters.AsNoTracking().Where(item => item.SeasonId == season.Id).ToListAsync(cancellationToken)).ToDictionary(item => item.MembershipId);
        var coaches = memberships.Select(membership =>
        {
            users.TryGetValue(membership.UserId, out var user);
            rosters.TryGetValue(membership.Id, out var roster);
            var teams = roster?.Teams.ToList() ?? [];
            var status = roster is null || teams.Count == 0 ? "missing" : roster.IsLocked ? "locked" : roster.IsComplete ? "complete" : "incomplete";
            return new CommissionerCoach(membership.Id.Value, user?.Name ?? "Unknown coach", user?.Email.Value ?? "", status, teams.Count, teams.Select(team => team.Value).ToList(),
                teams.Select(team => names.GetValueOrDefault(team) ?? "Unknown team").Order().ToList(), roster?.IsLocked ?? false, roster?.LockedAt, roster?.Revision ?? 0);
        }).OrderBy(coach => coach.Status switch { "missing" => 0, "incomplete" => 1, "complete" => 2, _ => 3 }).ThenBy(coach => coach.Name, StringComparer.Ordinal).ToList();
        return new(season.Id.Value, season.Name, season.RosterDraftDeadline, season.RosterDraftDeadline is { } deadline ? EasternTime.ToLocalText(deadline) : null, season.RosterDraftOpen(now), now,
            catalog.Select(team => new CatalogEntry(team.TeamId.Value, team.Name, team.Division)).ToList(), coaches);
    }
}
