using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Platform;

namespace Swan.Services.Picks;

public sealed record PickSubmission(MembershipId MembershipId, string Name, int Starts, int StartLimit, int Dogs, int DogLimit)
{
    public bool Complete => Starts == StartLimit && Dogs == DogLimit;
}
public sealed record AdminPick(Guid TeamId, string Team, Guid GameId, int? PointValue, decimal? Spread, bool Locked, string? Result);
public sealed record AdminCoachPicks(Guid MembershipId, string Name, IReadOnlyList<AdminPick> Starts, IReadOnlyList<AdminPick> Dogs, bool Complete);
public sealed record AdminWeekPicks(IReadOnlyList<AdminCoachPicks> Coaches);
public sealed record AdminPickOverride(IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> DogTeamIds, string Reason);

public interface IAdminWeekPickService
{
    Task<Result<AdminWeekPicks>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default);
    Task<Result<AdminWeekPicks>> OverrideAsync(UserId actor, WeekId weekId, MembershipId membershipId, AdminPickOverride request, CancellationToken cancellationToken = default);
}

public sealed class AdminWeekPickService(SwanDbContext database, IClock clock, IAuditService audit) : IAdminWeekPickService
{
    public async Task<Result<AdminWeekPicks>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default)
    {
        var week = await database.Weeks.AsNoTracking().SingleOrDefaultAsync(value => value.Id == weekId, cancellationToken);
        if (week is null) return Result<AdminWeekPicks>.Failure("week.missing", "Week does not exist.");
        var memberships = await database.Memberships.AsNoTracking().Where(value => value.SeasonId == week.SeasonId && value.Role == MembershipRole.Coach && value.IsActive).ToListAsync(cancellationToken);
        var users = await database.Users.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var ballots = await database.Ballots.AsNoTracking().Where(value => value.WeekId == weekId).ToDictionaryAsync(value => value.MembershipId, cancellationToken);
        var teams = await database.Teams.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var games = week.Games.ToDictionary(value => value.Id);
        var doubleDog = (await database.Seasons.AsNoTracking().SingleAsync(value => value.Id == week.SeasonId, cancellationToken)).DoubleDogWeekNumbers.Contains(week.Number);
        AdminPick Pick(TeamId teamId, GameId gameId, int? points, decimal? spread, DateTimeOffset lockTime)
        {
            games.TryGetValue(gameId, out var game);
            var result = week.State == WeekState.Final && game?.Result is { } score ? $"{score.HomeScore}-{score.AwayScore}" : null;
            return new(teamId.Value, teams.GetValueOrDefault(teamId) ?? "Unknown team", gameId.Value, points, spread, !LockPolicy.CanChange(lockTime, clock.UtcNow), result);
        }
        var coaches = memberships.Select(member =>
        {
            ballots.TryGetValue(member.Id, out var ballot);
            var starts = ballot?.Picks.Select(value => Pick(value.TeamId, value.GameId, value.PointValue, null, value.LockTime)).ToList() ?? [];
            var dogs = ballot?.Dogs.Select(value => Pick(value.TeamId, value.GameId, null, games.GetValueOrDefault(value.GameId)?.ApprovedLine?.Value, value.LockTime)).ToList() ?? [];
            return new AdminCoachPicks(member.Id.Value, users.GetValueOrDefault(member.UserId) ?? "Unknown coach", starts, dogs, starts.Count == 3 && dogs.Count == (doubleDog ? 2 : 1));
        }).OrderBy(value => value.Name, StringComparer.Ordinal).ToList();
        return Result<AdminWeekPicks>.Success(new(coaches));
    }

    public async Task<Result<AdminWeekPicks>> OverrideAsync(UserId actor, WeekId weekId, MembershipId membershipId, AdminPickOverride request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<AdminWeekPicks>.Failure("pick.override.reason", "A commissioner override requires a reason.");
        var week = await database.Weeks.SingleOrDefaultAsync(value => value.Id == weekId, cancellationToken);
        if (week is null) return Result<AdminWeekPicks>.Failure("week.missing", "Week does not exist.");
        if (week.State is not (WeekState.Open or WeekState.Locked)) return Result<AdminWeekPicks>.Failure("week.state", "Picks can be overridden only while the week is open or locked.");
        var season = await database.Seasons.SingleAsync(value => value.Id == week.SeasonId, cancellationToken);
        var membership = await database.Memberships.SingleOrDefaultAsync(value => value.Id == membershipId && value.SeasonId == week.SeasonId && value.Role == MembershipRole.Coach, cancellationToken);
        if (membership is null) return Result<AdminWeekPicks>.Failure("membership.missing", "Coach membership does not exist for this week.");
        var roster = await database.Rosters.SingleOrDefaultAsync(value => value.MembershipId == membershipId && value.SeasonId == week.SeasonId, cancellationToken);
        if (roster is null) return Result<AdminWeekPicks>.Failure("roster.missing", "Coach roster does not exist.");
        var games = week.Games.Where(game => !game.Excluded).ToList();
        var finishes = await database.SeasonTeams.Where(value => value.SeasonId == week.SeasonId).ToDictionaryAsync(value => value.TeamId, value => value.FinishPosition, cancellationToken);
        var picks = new List<TeamPick>();
        foreach (var id in request.TeamIds)
        {
            var team = new TeamId(id); var game = games.SingleOrDefault(value => value.HomeTeamId == team || value.AwayTeamId == team);
            if (game is null || !roster.Teams.Contains(team) || !finishes.TryGetValue(team, out var points)) return Result<AdminWeekPicks>.Failure("pick.invalid", "Each team pick must be a roster team playing in this week.");
            picks.Add(TeamPick.ForGame(team, game.Id, game.Kickoff.Value, season.Settings, games.Min(value => value.Kickoff.Value)) with { PointValue = points });
        }
        var dogs = new List<DogPick>();
        foreach (var id in request.DogTeamIds)
        {
            var team = new TeamId(id); var game = games.SingleOrDefault(value => value.HomeTeamId == team || value.AwayTeamId == team);
            if (game is null || !week.DogOptions.Any(value => value.TeamId == team)) return Result<AdminWeekPicks>.Failure("dog.invalid", "Each dog must be an approved dog option for this week.");
            dogs.Add(DogPick.ForGame(team, game.Id, game.Kickoff.Value));
        }
        var ballot = await database.Ballots.SingleOrDefaultAsync(value => value.MembershipId == membershipId && value.WeekId == weekId, cancellationToken) ?? new Ballot(new BallotId(Guid.NewGuid()), membershipId, weekId);
        var before = string.Join(',', ballot.Picks.Select(value => value.TeamId.Value));
        var result = ballot.OverrideByCommissioner(picks, dogs, week.IsDoubleDog(season), roster.Teams.ToHashSet(), clock.UtcNow, request.Reason);
        if (!result.IsSuccess) return Result<AdminWeekPicks>.Failure(result.Error!.Code, result.Error.Message);
        if (ballot.Id != default && !database.Ballots.Local.Contains(ballot)) database.Ballots.Add(ballot);
        await database.SaveChangesAsync(cancellationToken);
        await audit.AppendAsync(actor, "ballot.overridden", ballot.Id.Value.ToString(), before, string.Join(',', ballot.Picks.Select(value => value.TeamId.Value)), request.Reason.Trim(), cancellationToken);
        return await GetAsync(weekId, cancellationToken);
    }
}
