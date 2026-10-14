using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Swan.Core.Common;
using Swan.Core;
using Swan.Core.Identity;
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

// One imported week on the Weeks and dogs page. Reason is the human-facing text ("" for an ordinary week).
public sealed record DoubleDogWeek(int Week, bool Selected, bool Required, bool Suggested, string Reason);

public sealed record DoubleDogPlan(IReadOnlyList<DoubleDogWeek> Weeks, IReadOnlyList<int> Selected);

// A pool's payout place with its calculated dollar amount for the current pot.
public sealed record PoolPayoutPlace(int Position, decimal Percent, decimal Amount);

public sealed record PoolPayout(string Pool, decimal Stake, decimal Pot, IReadOnlyList<PoolPayoutPlace> Places);

// Configured is false until an administrator saves the plans; the calculator counts this season's active coach memberships only.
public sealed record FinesAndPayoutsPlan(bool Configured, decimal? MissedPickFine, int ActiveCoaches, IReadOnlyList<PoolPayout> Pools);

public sealed record LeagueSettingsPlan(bool TeamMayAlsoBeDog, IReadOnlyList<PointColor> PointColors, RecordHolderMode RecordHolder);

// A Setup-season Coach membership with its person; Active is false once an administrator has unselected them (the membership is kept).
public sealed record SetupCoach(Guid MembershipId, Guid UserId, string Name, string Email, bool Active);

// A person from the most recent earlier season who could be carried forward; Selected means they are already an active Coach of the Setup season.
public sealed record CoachCandidate(Guid UserId, string Name, string Email, bool Selected);

public sealed record PriorSeason(Guid Id, int Year, string Name);

public sealed record CoachesPlan(PriorSeason? Prior, IReadOnlyList<SetupCoach> Coaches, IReadOnlyList<CoachCandidate> Candidates, int ActiveCoaches);

public sealed record ReviewIssue(string Code, string Message, string Step);

public sealed record ReviewBasics(int Year, string Name, bool IsSimulation);

public sealed record ReviewPool(string Name, decimal Stake);

public sealed record ReviewMoney(decimal BuyIn, IReadOnlyList<ReviewPool> Pools);

public sealed record ReviewTeams(int Entered, int Total, IReadOnlyList<SetupTeam> Teams);

public sealed record ReviewSchedule(int Weeks, int Games, bool Complete, DateTimeOffset? FirstKickoff, DateTimeOffset? LastKickoff);

// Everything the final page shows, read from saved data only. Completed means the wizard was finished; Ready is whether it could be finished right now.
public sealed record SetupReview(
    bool Completed,
    bool Ready,
    IReadOnlyList<ReviewIssue> Blockers,
    IReadOnlyList<ReviewIssue> Warnings,
    ReviewBasics Basics,
    ReviewMoney Money,
    FinesAndPayoutsPlan Payouts,
    TimeOnly? TeamPickDeadlineEastern,
    ReviewTeams Teams,
    ReviewSchedule Schedule,
    IReadOnlyList<int> DoubleDogWeeks,
    LeagueSettingsPlan LeagueSettings,
    int ActiveCoaches);

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
    Task<Result<DoubleDogPlan>> GetSetupDoubleDogWeeksAsync(CancellationToken cancellationToken = default);
    Task<Result<DoubleDogPlan>> UpdateSetupDoubleDogWeeksAsync(UserId actor, IReadOnlyList<int> weeks, CancellationToken cancellationToken = default);
    Task<Result<FinesAndPayoutsPlan>> GetSetupFinesAndPayoutsAsync(CancellationToken cancellationToken = default);
    Task<Result<SetupReview>> GetSetupReviewAsync(CancellationToken cancellationToken = default);
    Task<Result<SetupReview>> FinishSetupAsync(UserId actor, CancellationToken cancellationToken = default);
    Task<Result<CoachesPlan>> GetSetupCoachesAsync(CancellationToken cancellationToken = default);
    Task<Result<CoachesPlan>> UpdateSetupCoachesAsync(UserId actor, IReadOnlyList<CoachEntry> coaches, CancellationToken cancellationToken = default);
    Task<Result<LeagueSettingsPlan>> GetSetupLeagueSettingsAsync(CancellationToken cancellationToken = default);
    Task<Result<LeagueSettingsPlan>> UpdateSetupLeagueSettingsAsync(UserId actor, LeagueRules rules, CancellationToken cancellationToken = default);
    Task<Result<FinesAndPayoutsPlan>> UpdateSetupFinesAndPayoutsAsync(UserId actor, Money missedPickFine, IReadOnlyList<PayoutPlan> plans, CancellationToken cancellationToken = default);
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

    // The year is part of the key: the provider was asked for that year, and the Setup year can be edited without changing the season id.
    private static string PreviewKey(SeasonId seasonId, int year, string fingerprint) => $"schedule-preview:{seasonId.Value}:{year}:{fingerprint}";

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
        previews.Set(PreviewKey(setup.Id, setup.Year, fingerprint), fetched.Value, PreviewLifetime);
        return Result<SchedulePlan>.Success(await ToPlanAsync(setup.Year, built.Value!, imported: false, fingerprint, cancellationToken));
    }

    // Saves the previewed schedule as Week aggregates, atomically. It imports what the preview held and never contacts the provider.
    public async Task<Result<SchedulePlan>> ImportScheduleAsync(UserId actor, string fingerprint, CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        if (setup is null) return Result<SchedulePlan>.Failure("season.missing", "No season is in Setup.");
        if (!previews.TryGetValue(PreviewKey(setup.Id, setup.Year, fingerprint), out ProviderSchedule? fetched) || fetched is null)
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

    // Reads only the imported schedule (never the provider): every regular-season week with whether it is selected, required (Week 2) or suggested (Thanksgiving, Christmas).
    public async Task<Result<DoubleDogPlan>> GetSetupDoubleDogWeeksAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        if (setup is null) return Result<DoubleDogPlan>.Failure("season.missing", "No season is in Setup.");
        return await DoubleDogPlanAsync(setup, cancellationToken);
    }

    // Saves the whole selection under the Setup row lock; the legacy count follows it, and an unchanged selection writes nothing.
    public Task<Result<DoubleDogPlan>> UpdateSetupDoubleDogWeeksAsync(UserId actor, IReadOnlyList<int> weeks, CancellationToken cancellationToken = default) =>
        InSetupAsync(async season =>
        {
            var planned = await DoubleDogPlanAsync(season, cancellationToken);
            if (!planned.IsSuccess) return planned;
            var before = season.DoubleDogWeekNumbers.ToList();
            var edited = season.SaveDoubleDogWeeks(weeks);
            if (!edited.IsSuccess) return Result<DoubleDogPlan>.Failure(edited.Error!.Code, edited.Error.Message);
            if (!before.SequenceEqual(season.DoubleDogWeekNumbers))
                await audit.AppendAsync(actor, "season.setup.double-dogs.updated", season.Id.Value.ToString(), before, season.DoubleDogWeekNumbers, cancellationToken: cancellationToken);
            return await DoubleDogPlanAsync(season, cancellationToken);
        }, cancellationToken);

    public async Task<Result<SetupReview>> GetSetupReviewAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        return setup is null ? Result<SetupReview>.Failure("season.missing", "No season is in Setup.") : Result<SetupReview>.Success(await ReviewAsync(setup, cancellationToken));
    }

    // Finishing is bookkeeping: the blockers are re-checked under the Setup row lock, the season stays in Setup, and nothing is activated, sent or created.
    public Task<Result<SetupReview>> FinishSetupAsync(UserId actor, CancellationToken cancellationToken = default) =>
        InSetupAsync(async season =>
        {
            var review = await ReviewAsync(season, cancellationToken);
            if (!review.Ready) return Result<SetupReview>.Failure("setup.incomplete", $"Setup cannot be finished yet: {string.Join(" ", review.Blockers.Select(issue => issue.Message))}");
            if (season.SetupStep == SetupStep.Completed) return Result<SetupReview>.Success(review);
            var completed = season.CompleteSetup();
            if (!completed.IsSuccess) return Result<SetupReview>.Failure(completed.Error!.Code, completed.Error.Message);
            await audit.AppendAsync(actor, "season.setup.completed", season.Id.Value.ToString(), before: null, after: new
            {
                review.Basics.Name,
                review.Basics.Year,
                review.Basics.IsSimulation,
                BuyIn = review.Money.BuyIn,
                MissedPickFine = review.Payouts.MissedPickFine,
                TeamsEntered = review.Teams.Entered,
                ScheduleWeeks = review.Schedule.Weeks,
                ScheduleGames = review.Schedule.Games,
                review.DoubleDogWeeks,
                review.ActiveCoaches,
                review.LeagueSettings,
                Warnings = review.Warnings.Select(issue => issue.Code).ToArray(),
            }, cancellationToken: cancellationToken);
            return Result<SetupReview>.Success(review with { Completed = true });
        }, cancellationToken);

    private async Task<SetupReview> ReviewAsync(Season season, CancellationToken cancellationToken)
    {
        var teams = (await ListTeamsAsync(season.Id, cancellationToken)).Teams;
        var catalog = await database.Teams.AsNoTracking().ToListAsync(cancellationToken);
        var saved = await database.SeasonTeams.AsNoTracking().Where(team => team.SeasonId == season.Id).ToListAsync(cancellationToken);
        var teamsComplete = SetupTeamFinishes.Create(season.Id, catalog, saved.Select(team => new TeamFinish(team.TeamId, team.FinishPosition)).ToList()).IsSuccess;
        var weeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == season.Id).ToListAsync(cancellationToken);
        var kickoffs = weeks.SelectMany(week => week.Games.Select(game => game.Kickoff.Value)).ToList();
        var scheduleComplete = weeks.Select(week => week.Number).Order().SequenceEqual(Enumerable.Range(1, DoubleDogWeekRules.RegularSeasonWeeks)) && weeks.All(week => week.Games.Count > 0);
        var coaches = (await CoachesAsync(season, cancellationToken)).ActiveCoaches;
        var dogs = await DoubleDogPlanAsync(season, cancellationToken);
        var holidayWeeks = dogs.IsSuccess ? dogs.Value!.Weeks.Where(week => week.Suggested && !week.Required && !week.Selected).Select(week => week.Week).ToList() : [];
        var readiness = SetupReadiness.Evaluate(season, new SetupFacts(teams.Count(team => team.FinishPosition is not null), teams.Count, teamsComplete, weeks.Count, scheduleComplete, coaches, holidayWeeks));
        static ReviewIssue Issue(SetupIssue issue) => new(issue.Code, issue.Message, issue.Step.ToString());
        return new SetupReview(
            season.SetupStep == SetupStep.Completed,
            readiness.Ready,
            readiness.Blockers.Select(Issue).ToList(),
            readiness.Warnings.Select(Issue).ToList(),
            new ReviewBasics(season.Year, season.Name, season.IsSimulation),
            new ReviewMoney(season.Settings.BuyIn.Amount, season.Pools.Select(pool => new ReviewPool(pool.Name, pool.Stake.Amount)).ToList()),
            await FinesAndPayoutsAsync(season, cancellationToken),
            season.Settings.TeamPickDeadlineEastern,
            new ReviewTeams(teams.Count(team => team.FinishPosition is not null), teams.Count, teams),
            new ReviewSchedule(weeks.Count, weeks.Sum(week => week.Games.Count), scheduleComplete, kickoffs.Count == 0 ? null : kickoffs.Min(), kickoffs.Count == 0 ? null : kickoffs.Max()),
            season.DoubleDogWeekNumbers.ToList(),
            ToPlan(season.Settings.Rules),
            coaches);
    }

    public async Task<Result<CoachesPlan>> GetSetupCoachesAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        return setup is null ? Result<CoachesPlan>.Failure("season.missing", "No season is in Setup.") : Result<CoachesPlan>.Success(await CoachesAsync(setup, cancellationToken));
    }

    // The complete desired coach list: people are reused by email (never renamed or re-roled), new people become Coach users, and unlisted Setup-season Coach memberships are made inactive, not deleted. Nothing is emailed and no roster is created.
    public async Task<Result<CoachesPlan>> UpdateSetupCoachesAsync(UserId actor, IReadOnlyList<CoachEntry> coaches, CancellationToken cancellationToken = default)
    {
        var valid = CoachRoster.Validate(coaches);
        if (!valid.IsSuccess) return Result<CoachesPlan>.Failure(valid.Error!.Code, valid.Error.Message);
        return await InSetupAsync(async season =>
        {
            var emails = valid.Value!.Select(coach => coach.Email.Value).ToArray();
            var found = await database.Users.FromSqlInterpolated($"""select * from identity.users where lower("Email") = any({emails})""").ToListAsync(cancellationToken);
            var memberships = await database.Memberships.Where(membership => membership.SeasonId == season.Id).ToListAsync(cancellationToken);
            var people = await database.Users.AsNoTracking().Where(user => memberships.Select(membership => membership.UserId).Contains(user.Id)).ToListAsync(cancellationToken);
            people.AddRange(found.Where(user => people.All(person => person.Id != user.Id)));
            List<CoachState> State() => memberships.Where(membership => membership.Role == MembershipRole.Coach)
                .Select(membership => new CoachState(people.First(person => person.Id == membership.UserId).Email.Value, people.First(person => person.Id == membership.UserId).Name, membership.IsActive))
                .OrderBy(state => state.Email, StringComparer.Ordinal).ToList();
            var before = State();

            // Resolve and check everyone first so a bad row changes nothing.
            var plan = new List<(ValidCoach Coach, User? Existing)>();
            foreach (var coach in valid.Value!)
            {
                var matches = found.Where(user => string.Equals(user.Email.Value, coach.Email.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 1) return Result<CoachesPlan>.Failure("season.coaches", $"More than one person already uses {coach.Email.Value}; resolve the duplicate before adding them.");
                var existing = matches.SingleOrDefault();
                if (existing is { IsActive: false }) return Result<CoachesPlan>.Failure("season.coaches", $"{coach.Email.Value} belongs to a deactivated person and cannot be added as a coach.");
                if (existing is not null && memberships.FirstOrDefault(membership => membership.UserId == existing.Id) is { Role: not MembershipRole.Coach })
                    return Result<CoachesPlan>.Failure("season.coaches", $"{coach.Email.Value} is already an administrator or commissioner of this season and cannot also be a coach.");
                plan.Add((coach, existing));
            }

            var desired = new HashSet<UserId>();
            foreach (var (coach, existing) in plan)
            {
                var user = existing ?? new User(new UserId(Guid.NewGuid()), coach.Email, coach.Name, UserRole.Coach);
                if (existing is null) { database.Users.Add(user); people.Add(user); }
                desired.Add(user.Id);
                var membership = memberships.FirstOrDefault(value => value.UserId == user.Id);
                if (membership is null)
                {
                    membership = new Membership(new MembershipId(Guid.NewGuid()), user.Id, season.Id, MembershipRole.Coach);
                    database.Memberships.Add(membership);
                    memberships.Add(membership);
                }
                else if (!membership.IsActive) membership.Reactivate();
            }

            foreach (var membership in memberships.Where(value => value.Role == MembershipRole.Coach && value.IsActive && !desired.Contains(value.UserId)))
                membership.Deactivate();

            var after = State();
            if (!before.SequenceEqual(after))
                await audit.AppendAsync(actor, "season.setup.coaches.updated", season.Id.Value.ToString(), before, after, cancellationToken: cancellationToken);
            return Result<CoachesPlan>.Success(await CoachesAsync(season, cancellationToken));
        }, cancellationToken);
    }

    private sealed record CoachState(string Email, string Name, bool Active);

    private async Task<CoachesPlan> CoachesAsync(Season setup, CancellationToken cancellationToken)
    {
        var memberships = await database.Memberships.AsNoTracking().Where(membership => membership.SeasonId == setup.Id && membership.Role == MembershipRole.Coach).ToListAsync(cancellationToken);
        var users = (await database.Users.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(user => user.Id);
        var coaches = memberships.Select(membership => new SetupCoach(membership.Id.Value, membership.UserId.Value, users[membership.UserId].Name, users[membership.UserId].Email.Value, membership.IsActive))
            .OrderBy(coach => coach.Name, StringComparer.OrdinalIgnoreCase).ThenBy(coach => coach.Email, StringComparer.Ordinal).ToList();
        // The newest earlier season, by year then state then id so the choice is the same every time; nothing is copied automatically.
        var prior = (await database.Seasons.AsNoTracking().Where(season => season.State != SeasonState.Setup).ToListAsync(cancellationToken))
            .OrderByDescending(season => season.Year).ThenByDescending(season => season.State).ThenBy(season => season.Id.Value).FirstOrDefault();
        var active = coaches.Where(coach => coach.Active).Select(coach => coach.UserId).ToHashSet();
        IReadOnlyList<CoachCandidate> candidates = [];
        if (prior is not null)
        {
            var prior_ = await database.Memberships.AsNoTracking().Where(membership => membership.SeasonId == prior.Id && membership.Role == MembershipRole.Coach && membership.IsActive).ToListAsync(cancellationToken);
            candidates = prior_.Select(membership => users[membership.UserId]).Where(user => user.IsActive)
                .Select(user => new CoachCandidate(user.Id.Value, user.Name, user.Email.Value, active.Contains(user.Id.Value)))
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ThenBy(candidate => candidate.Email, StringComparer.Ordinal).ToList();
        }

        return new CoachesPlan(prior is null ? null : new PriorSeason(prior.Id.Value, prior.Year, prior.Name), coaches, candidates, coaches.Count(coach => coach.Active));
    }

    public async Task<Result<LeagueSettingsPlan>> GetSetupLeagueSettingsAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        return setup is null ? Result<LeagueSettingsPlan>.Failure("season.missing", "No season is in Setup.") : Result<LeagueSettingsPlan>.Success(ToPlan(setup.Settings.Rules));
    }

    // R17 to R19 are saved together under the Setup row lock; an identical save writes nothing.
    public Task<Result<LeagueSettingsPlan>> UpdateSetupLeagueSettingsAsync(UserId actor, LeagueRules rules, CancellationToken cancellationToken = default) =>
        InSetupAsync(async season =>
        {
            var before = season.Settings.Rules;
            var edited = season.SaveLeagueRules(rules);
            if (!edited.IsSuccess) return Result<LeagueSettingsPlan>.Failure(edited.Error!.Code, edited.Error.Message);
            if (before != season.Settings.Rules)
                await audit.AppendAsync(actor, "season.setup.league-settings.updated", season.Id.Value.ToString(), ToPlan(before), ToPlan(season.Settings.Rules), cancellationToken: cancellationToken);
            return Result<LeagueSettingsPlan>.Success(ToPlan(season.Settings.Rules));
        }, cancellationToken);

    private static LeagueSettingsPlan ToPlan(LeagueRules rules) => new(rules.TeamMayAlsoBeDog, rules.PointColors.Entries, rules.RecordHolder);

    public async Task<Result<FinesAndPayoutsPlan>> GetSetupFinesAndPayoutsAsync(CancellationToken cancellationToken = default)
    {
        var setup = await database.Seasons.AsNoTracking().FirstOrDefaultAsync(season => season.State == SeasonState.Setup, cancellationToken);
        return setup is null ? Result<FinesAndPayoutsPlan>.Failure("season.missing", "No season is in Setup.") : Result<FinesAndPayoutsPlan>.Success(await FinesAndPayoutsAsync(setup, cancellationToken));
    }

    // The fine and every pool's plan are saved together under the Setup row lock; an unchanged configuration writes nothing. This only configures and calculates: no money moves.
    public Task<Result<FinesAndPayoutsPlan>> UpdateSetupFinesAndPayoutsAsync(UserId actor, Money missedPickFine, IReadOnlyList<PayoutPlan> plans, CancellationToken cancellationToken = default) =>
        InSetupAsync(async season =>
        {
            var before = Describe(season);
            var edited = season.SaveFinesAndPayouts(missedPickFine, plans);
            if (!edited.IsSuccess) return Result<FinesAndPayoutsPlan>.Failure(edited.Error!.Code, edited.Error.Message);
            var after = Describe(season);
            if (!SameValues(before, after))
                await audit.AppendAsync(actor, "season.setup.fines-and-payouts.updated", season.Id.Value.ToString(), before, after, cancellationToken: cancellationToken);
            return Result<FinesAndPayoutsPlan>.Success(await FinesAndPayoutsAsync(season, cancellationToken));
        }, cancellationToken);

    private sealed record Described(decimal? MissedPickFine, IReadOnlyList<PayoutPlan> Plans);

    private static Described Describe(Season season) => new(season.MissedPickFine?.Amount, season.PayoutPlans);

    // Compared by value, not text: 5 and 5.00 (or 70 and 70.00) are the same configuration and must not look like a change.
    private static bool SameValues(Described before, Described after) =>
        before.MissedPickFine == after.MissedPickFine
        && before.Plans.Select(plan => (plan.Pool, Places: plan.Places.Select(place => (place.Position, place.Percent)).ToList()))
            .SequenceEqual(after.Plans.Select(plan => (plan.Pool, Places: plan.Places.Select(place => (place.Position, place.Percent)).ToList())), new PlanComparer());

    private sealed class PlanComparer : IEqualityComparer<(string Pool, List<(int Position, decimal Percent)> Places)>
    {
        public bool Equals((string Pool, List<(int Position, decimal Percent)> Places) left, (string Pool, List<(int Position, decimal Percent)> Places) right) =>
            left.Pool == right.Pool && left.Places.SequenceEqual(right.Places);

        public int GetHashCode((string Pool, List<(int Position, decimal Percent)> Places) value) => value.Pool.GetHashCode();
    }

    private async Task<FinesAndPayoutsPlan> FinesAndPayoutsAsync(Season season, CancellationToken cancellationToken)
    {
        var coaches = await database.Memberships.AsNoTracking().CountAsync(membership => membership.SeasonId == season.Id && membership.Role == MembershipRole.Coach && membership.IsActive, cancellationToken);
        var pools = PayoutRules.Pools.Select(name =>
        {
            var stake = season.Pools.FirstOrDefault(pool => pool.Name == name)?.Stake.Amount ?? 0m;
            var pot = stake * coaches;
            var places = season.PayoutPlans.FirstOrDefault(plan => plan.Pool == name)?.Places ?? [];
            var amounts = PayoutRules.Split(pot, places);
            return new PoolPayout(name, stake, pot, places.OrderBy(place => place.Position).Select((place, index) => new PoolPayoutPlace(place.Position, place.Percent, amounts[index])).ToList());
        }).ToList();
        return new FinesAndPayoutsPlan(season.PayoutPlans.Count > 0, season.MissedPickFine?.Amount, coaches, pools);
    }

    private async Task<Result<DoubleDogPlan>> DoubleDogPlanAsync(Season season, CancellationToken cancellationToken)
    {
        var weeks = await database.Weeks.AsNoTracking().Where(week => week.SeasonId == season.Id).ToListAsync(cancellationToken);
        if (weeks.Select(week => week.Number).Order().SequenceEqual(Enumerable.Range(1, DoubleDogWeekRules.RegularSeasonWeeks)) is false)
            return Result<DoubleDogPlan>.Failure("schedule.missing", "Import the full regular-season schedule before choosing double-dog weeks.");
        var kickoffs = weeks.Select(week => new WeekKickoffs(week.Number, week.Games.Select(game => game.Kickoff.Value).ToList())).ToList();
        // The imported schedule's own year, not the Setup year: the year can be edited after an import and must not move the holidays.
        var holidays = DoubleDogWeekRules.Holidays(DoubleDogWeekRules.ScheduleYear(kickoffs) ?? season.Year, kickoffs);
        string Holiday(string name, DateOnly date) => $"{name} ({date.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture)})";
        var rows = Enumerable.Range(1, DoubleDogWeekRules.RegularSeasonWeeks).Select(number =>
        {
            var contains = new List<string>();
            if (holidays.ThanksgivingWeek == number) contains.Add(Holiday("Thanksgiving", holidays.Thanksgiving));
            if (holidays.ChristmasWeek == number) contains.Add(Holiday("Christmas", holidays.Christmas));
            var required = number == 2;
            var reasons = new List<string>();
            if (required) reasons.Add("Required by the rules");
            if (contains.Count > 0) reasons.Add($"{(required ? "also suggested" : "Suggested")} · contains {string.Join(" and ", contains)}");
            return new DoubleDogWeek(number, season.DoubleDogWeekNumbers.Contains(number), required, contains.Count > 0, string.Join(" · ", reasons));
        }).ToList();
        return Result<DoubleDogPlan>.Success(new DoubleDogPlan(rows, season.DoubleDogWeekNumbers.ToList()));
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
