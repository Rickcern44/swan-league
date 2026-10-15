using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Picks;

// Ids are plain Guids and states are strings: these records are what the pick desk shows and are served as they are.
public sealed record DeskWeek(Guid Id, int Number, DateTimeOffset Deadline, bool IsDoubleDogWeek, int PickLimit, int DogLimit);
public sealed record DeskGame(Guid TeamId, string Team, Guid GameId, string Opponent, bool IsHome, DateTimeOffset Kickoff, DateTimeOffset LockTime, bool Locked, int PointValue, bool Selected);
public sealed record DeskDog(Guid TeamId, string Team, Guid GameId, string Opponent, bool IsHome, DateTimeOffset Kickoff, DateTimeOffset LockTime, bool Locked, decimal Spread, bool Selected);

// State is "open", "no-membership" (no active coach membership in a live season), "no-week" (no open regular week) or "no-roster" (the coach has no roster teams yet).
public sealed record PickDesk(string State, string? SeasonName, DeskWeek? Week, int Revision, DateTimeOffset ServerTime, IReadOnlyList<DeskGame> Games, IReadOnlyList<string> NotPlaying, IReadOnlyList<DeskDog> Dogs);

public sealed record SavePicks(Guid WeekId, int ExpectedRevision, IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> DogTeamIds);

public interface ICoachPickService
{
    Task<Result<PickDesk>> GetAsync(UserId coach, CancellationToken cancellationToken = default);
    Task<Result<PickDesk>> SaveAsync(UserId coach, SavePicks request, CancellationToken cancellationToken = default);
}

// The coach's own desk: who they are, which week is open and what they may pick are all decided here from saved data. Only the acting coach's ballot is ever read or written.
// If several live seasons exist the newest year wins; if several regular weeks are Open the lowest-numbered one is the desk.
public sealed class CoachPickService(SwanDbContext database, IClock clock, IAuditService audit) : ICoachPickService
{
    public async Task<Result<PickDesk>> GetAsync(UserId coach, CancellationToken cancellationToken = default)
    {
        var context = await ResolveAsync(coach, cancellationToken);
        return Result<PickDesk>.Success(await BuildAsync(context, cancellationToken));
    }

    public async Task<Result<PickDesk>> SaveAsync(UserId coach, SavePicks request, CancellationToken cancellationToken = default)
    {
        // The retrying execution strategy only allows a user transaction inside ExecuteAsync, which may rerun the delegate.
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var first = await ResolveAsync(coach, cancellationToken);
            if (first.Membership is null || first.Week is null || first.Roster is null) return Result<PickDesk>.Failure("pick.unavailable", "There is no open week with a roster to pick for.");
            // The row lock makes two saves by the same coach take turns, so the revision check below is reliable.
            await database.Memberships.FromSqlInterpolated($"""select * from league.memberships where "Id" = {first.Membership.Id.Value} for update""").ToListAsync(cancellationToken);
            database.ChangeTracker.Clear();
            var context = await ResolveAsync(coach, cancellationToken);
            if (context.Week is null || context.Membership is null || context.Roster is null) return Result<PickDesk>.Failure("pick.unavailable", "There is no open week with a roster to pick for.");
            if (context.Week.Id.Value != request.WeekId) return Result<PickDesk>.Failure("week.stale", "That week is no longer the open week. Reload to see the current one.");
            var ballot = context.Ballot ?? new Ballot(new BallotId(Guid.NewGuid()), context.Membership.Id, context.Week.Id);
            if (ballot.Revision != request.ExpectedRevision) return Result<PickDesk>.Failure("ballot.stale", "Your picks changed since this page loaded. Reload to see the saved ballot.");

            var rows = await RowsAsync(context, cancellationToken);
            var picks = new List<TeamPick>();
            foreach (var teamId in request.TeamIds)
            {
                var row = rows.Teams.FirstOrDefault(item => item.TeamId == teamId);
                if (row is null) return Result<PickDesk>.Failure("pick.invalid", "You can only start your own teams that play this week.");
                picks.Add(TeamPick.ForGame(new TeamId(row.TeamId), new GameId(row.GameId), row.Kickoff, context.Season!.Settings, rows.WeekStart) with { PointValue = row.PointValue });
            }

            var dogs = new List<DogPick>();
            foreach (var teamId in request.DogTeamIds)
            {
                var row = rows.Dogs.FirstOrDefault(item => item.TeamId == teamId);
                if (row is null) return Result<PickDesk>.Failure("dog.invalid", "That team is not one of this week's dog options.");
                dogs.Add(DogPick.ForGame(new TeamId(row.TeamId), new GameId(row.GameId), row.Kickoff));
            }

            var before = Summarize(ballot);
            var saved = ballot.Save(picks, dogs, context.Week.IsDoubleDog(context.Season!), context.Roster.Teams.ToHashSet(), clock.UtcNow);
            if (!saved.IsSuccess)
            {
                database.ChangeTracker.Clear();
                return Result<PickDesk>.Failure(saved.Error!.Code, saved.Error.Message);
            }

            if (context.Ballot is null) database.Ballots.Add(ballot);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                if (!before.Equals(Summarize(ballot))) await audit.AppendAsync(coach, "ballot.saved", ballot.Id.Value.ToString(), before, Summarize(ballot), cancellationToken: cancellationToken);
            }
            catch (DbUpdateException) when (context.Ballot is null)
            {
                // Only a concurrent first save can collide on the one-ballot-per-week index.
                database.ChangeTracker.Clear();
                return Result<PickDesk>.Failure("ballot.stale", "Your picks changed since this page loaded. Reload to see the saved ballot.");
            }

            var desk = await BuildAsync(await ResolveAsync(coach, cancellationToken), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<PickDesk>.Success(desk);
        });
    }

    private sealed record Context(Season? Season, Membership? Membership, Week? Week, Roster? Roster, Ballot? Ballot);

    private async Task<Context> ResolveAsync(UserId coach, CancellationToken cancellationToken)
    {
        var live = (await database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Live).ToListAsync(cancellationToken))
            .OrderByDescending(season => season.Year).ThenBy(season => season.Name, StringComparer.Ordinal).ThenBy(season => season.Id.Value).ToList();
        var seasonIds = live.Select(season => season.Id).ToList();
        var memberships = await database.Memberships.Where(membership => membership.UserId == coach && membership.Role == MembershipRole.Coach && membership.IsActive && seasonIds.Contains(membership.SeasonId)).ToListAsync(cancellationToken);
        var membership = live.Select(season => memberships.FirstOrDefault(item => item.SeasonId == season.Id)).FirstOrDefault(item => item is not null);
        if (membership is null) return new(null, null, null, null, null);
        var season = live.First(item => item.Id == membership.SeasonId);

        var week = (await database.Weeks.Where(item => item.SeasonId == season.Id && item.State == WeekState.Open).ToListAsync(cancellationToken))
            .Where(item => item.Type.Kind == WeekKind.Regular).OrderBy(item => item.Number).FirstOrDefault();
        if (week is null) return new(season, membership, null, null, null);

        var roster = await database.Rosters.FirstOrDefaultAsync(item => item.MembershipId == membership.Id && item.SeasonId == season.Id, cancellationToken);
        var ballot = await database.Ballots.FirstOrDefaultAsync(item => item.MembershipId == membership.Id && item.WeekId == week.Id, cancellationToken);
        return new(season, membership, week, roster, ballot);
    }

    private sealed record Rows(DateTimeOffset WeekStart, IReadOnlyList<DeskGame> Teams, IReadOnlyList<DeskDog> Dogs, IReadOnlyList<string> NotPlaying);

    private async Task<Rows> RowsAsync(Context context, CancellationToken cancellationToken)
    {
        var season = context.Season!;
        var week = context.Week!;
        var now = clock.UtcNow;
        var games = week.Games.Where(game => !game.Excluded).OrderBy(game => game.Kickoff.Value).ThenBy(game => game.Id.Value).ToList();
        var weekStart = games.Count > 0 ? games[0].Kickoff.Value : now;
        var teamIds = games.SelectMany(game => new[] { game.HomeTeamId, game.AwayTeamId }).Concat(context.Roster?.Teams ?? []).Distinct().ToList();
        var names = await database.Teams.AsNoTracking().Where(team => teamIds.Contains(team.Id)).ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        var finishes = await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == season.Id && teamIds.Contains(team.TeamId)).ToDictionaryAsync(team => team.TeamId, team => team.FinishPosition, cancellationToken);
        string Name(TeamId id) => names.GetValueOrDefault(id) ?? "Unknown team";
        var saved = context.Ballot;

        var teams = new List<DeskGame>();
        var playing = new HashSet<TeamId>();
        foreach (var game in games)
        {
            foreach (var (team, opponent, home) in new[] { (game.HomeTeamId, game.AwayTeamId, true), (game.AwayTeamId, game.HomeTeamId, false) })
            {
                if (context.Roster is null || !context.Roster.Teams.Contains(team) || !finishes.TryGetValue(team, out var points)) continue;
                playing.Add(team);
                var lockTime = LockPolicy.TeamPickLockTime(game.Kickoff.Value, season.Settings, weekStart);
                teams.Add(new(team.Value, Name(team), game.Id.Value, Name(opponent), home, game.Kickoff.Value, lockTime, !LockPolicy.CanChange(lockTime, now), points, saved?.Picks.Any(pick => pick.TeamId == team && pick.GameId == game.Id) ?? false));
            }
        }

        var dogs = new List<DeskDog>();
        foreach (var option in week.DogOptions)
        {
            var game = games.FirstOrDefault(item => item.HomeTeamId == option.TeamId || item.AwayTeamId == option.TeamId);
            if (game?.ApprovedLine is null) continue;
            var home = game.HomeTeamId == option.TeamId;
            var spread = home ? game.ApprovedLine.Value : -game.ApprovedLine.Value;
            if (spread <= 0) continue;
            var lockTime = LockPolicy.DogPickLockTime(game.Kickoff.Value);
            dogs.Add(new(option.TeamId.Value, Name(option.TeamId), game.Id.Value, Name(home ? game.AwayTeamId : game.HomeTeamId), home, game.Kickoff.Value, lockTime, !LockPolicy.CanChange(lockTime, now), spread, saved?.Dogs.Any(dog => dog.TeamId == option.TeamId && dog.GameId == game.Id) ?? false));
        }

        var notPlaying = (context.Roster?.Teams ?? []).Where(team => !playing.Contains(team)).Select(Name).Order().ToList();
        return new(weekStart, teams.OrderBy(row => row.Kickoff).ThenBy(row => row.Team, StringComparer.Ordinal).ToList(), dogs.OrderBy(row => row.Kickoff).ThenByDescending(row => row.Spread).ToList(), notPlaying);
    }

    private async Task<PickDesk> BuildAsync(Context context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (context.Membership is null) return new("no-membership", null, null, 0, now, [], [], []);
        if (context.Week is null) return new("no-week", context.Season!.Name, null, 0, now, [], [], []);
        var rows = await RowsAsync(context, cancellationToken);
        var week = new DeskWeek(context.Week.Id.Value, context.Week.Number, LockPolicy.WeekDeadline(context.Season!.Settings, rows.WeekStart), context.Week.IsDoubleDog(context.Season), 3, context.Week.IsDoubleDog(context.Season) ? 2 : 1);
        var revision = context.Ballot?.Revision ?? 0;
        if (context.Roster is null || context.Roster.Teams.Count == 0) return new("no-roster", context.Season.Name, week, revision, now, [], [], []);
        return new("open", context.Season.Name, week, revision, now, rows.Teams, rows.NotPlaying, rows.Dogs);
    }

    private static string Summarize(Ballot ballot) =>
        string.Join(",", ballot.Picks.Select(pick => pick.TeamId.Value).Order()) + "|" + string.Join(",", ballot.Dogs.Select(dog => dog.TeamId.Value).Order());
}

internal static class WeekExtensions
{
    // R7: the season's confirmed double-dog weeks allow two dogs.
    public static bool IsDoubleDog(this Week week, Season season) => season.DoubleDogWeekNumbers.Contains(week.Number);
}
