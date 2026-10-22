using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Communication;
using Swan.Core.Identity;
using Swan.Core.Finance;
using Swan.Core.Picks;
using Swan.Core.Scoring;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Data.Seed;
using Swan.Services.Development;
using Swan.Services.Identity;
using Swan.Services.Platform;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class DevelopmentResetServiceTests : IAsyncLifetime
{
    private static readonly UserId Admin = new(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d"));
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    public ValueTask InitializeAsync() => new(postgres.StartAsync());

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    // The development sample (a live season with rosters, two weeks of games and dog options) plus the things it does not create: a ballot, a score, a ledger, a notification and an announcement.
    private async Task SeedEverythingAsync()
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        await DevelopmentSeeder.SeedAsync(database);
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "Reset test season", true).Value!;
        season.SaveDoubleDogWeeks([2, 12, 17]);
        season.CompleteSetup();
        season.Start(hasActiveCommissioner: true);
        var membership = new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.Parse("00000000-0000-0000-0000-000000000201")), season.Id, MembershipRole.Coach);
        var teamIds = await database.Teams.OrderBy(team => team.Name).Select(team => team.Id).Take(9).ToListAsync();
        var week = new Week(new WeekId(Guid.NewGuid()), season.Id, 2, new WeekType(WeekKind.Regular, null, true));
        week.AddGame(new Game(new GameId(Guid.NewGuid()), teamIds[0], teamIds[1], Kickoff.Create(clock.UtcNow.AddDays(1)).Value!));
        database.Seasons.Add(season);
        database.Memberships.Add(membership);
        database.Weeks.Add(week);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var seededSeason = await database.Seasons.AsNoTracking().SingleAsync(item => item.Id == season.Id);
        var seededMembership = await database.Memberships.AsNoTracking().SingleAsync(item => item.Id == membership.Id);
        var seededWeek = await database.Weeks.AsNoTracking().SingleAsync(item => item.Id == week.Id);
        var game = seededWeek.Games.First();
        var roster = new Roster(new RosterId(Guid.NewGuid()), seededSeason.Id, seededMembership.Id);
        Assert.True(roster.SaveDraft(teamIds, teamIds.ToHashSet(), true).IsSuccess);
        Assert.True(roster.Lock(true, clock.UtcNow).IsSuccess);
        database.Rosters.Add(roster);
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), seededMembership.Id, seededWeek.Id);
        Assert.True(ballot.Save([new TeamPick(game.HomeTeamId, game.Id, clock.UtcNow.AddDays(1))], [], false, new[] { game.HomeTeamId }.ToHashSet(), clock.UtcNow).IsSuccess);
        database.Ballots.Add(ballot);
        var score = new WeekScore(new WeekScoreId(Guid.NewGuid()), seededSeason.Id, seededMembership.Id, seededWeek.Id);
        score.ReplaceLines([new ScoreLine(game.HomeTeamId, 1, ScoreSource.TeamPick, "r")]);
        database.WeekScores.Add(score);
        database.Ledgers.Add(new Ledger(new LedgerId(Guid.NewGuid()), seededSeason.Id));
        database.Notifications.Add(new Notification(new NotificationId(Guid.NewGuid()), seededMembership.Id, "week.opened", "Week 2 is open"));
        var preferences = new NotificationPreferences(seededMembership.Id);
        Assert.True(preferences.Mute(NotificationCategory.Reminders).IsSuccess);
        database.NotificationPreferences.Add(preferences);
        database.PushSubscriptions.Add(new PushSubscription(new PushSubscriptionId(Guid.NewGuid()), seededMembership.Id, "https://push.example/endpoint", "p256dh", "auth"));
        database.Announcements.Add(new Announcement(new AnnouncementId(Guid.NewGuid()), "Welcome", clock.UtcNow));
        await new AuditService(database, clock).AppendAsync(Admin, "season.setup.created", seededSeason.Id.Value.ToString());
        database.Sessions.Add(new UserSession(new SessionId(Guid.NewGuid()), Admin, new string('A', 64), clock.UtcNow, clock.UtcNow.AddDays(30)));
        await database.SaveChangesAsync();
    }

    private async Task<Dictionary<string, int>> CountsAsync()
    {
        await using var database = CreateDatabase();
        var tables = new[]
        {
            "league.seasons", "league.memberships", "schedule.season_teams", "schedule.weeks", "schedule.games", "schedule.dog_options", "picks.rosters", "picks.ballots", "picks.team_picks",
            "picks.dog_picks", "picks.pick_changes", "scoring.week_scores", "scoring.score_lines", "money.ledgers", "money.ledger_items", "money.pool_settlements", "money.pool_placements",
            "communication.notifications", "communication.notification_preferences", "communication.push_subscriptions", "communication.announcements", "platform.audit_entries", "schedule.teams", "identity.users", "identity.sessions", "identity.login_tokens",
        };
        var result = new Dictionary<string, int>();
#pragma warning disable EF1002 // the names are the literals above
        foreach (var table in tables)
            result[table] = await database.Database.SqlQueryRaw<int>($"select count(*)::int as \"Value\" from {table}").SingleAsync();
#pragma warning restore EF1002
        return result;
    }

    [Fact]
    public async Task ResettingClearsEverySeasonScopedTableAndKeepsTheCatalogThePeopleAndTheirSessions()
    {
        await SeedEverythingAsync();
        var before = await CountsAsync();
        Assert.All(new[] { "league.seasons", "league.memberships", "schedule.weeks", "schedule.games", "picks.rosters", "picks.ballots", "picks.team_picks", "scoring.week_scores", "scoring.score_lines", "money.ledgers", "communication.notifications", "communication.notification_preferences", "communication.push_subscriptions", "communication.announcements" }, table => Assert.True(before[table] > 0, table));

        var result = await Reset();

        Assert.True(result.IsSuccess, result.Error?.Message);
        var after = await CountsAsync();
        var cleared = new[]
        {
            "league.seasons", "league.memberships", "schedule.season_teams", "schedule.weeks", "schedule.games", "schedule.dog_options", "picks.rosters", "picks.ballots", "picks.team_picks",
            "picks.dog_picks", "picks.pick_changes", "scoring.week_scores", "scoring.score_lines", "money.ledgers", "money.ledger_items", "money.pool_settlements", "money.pool_placements",
            "communication.notifications", "communication.notification_preferences", "communication.push_subscriptions", "communication.announcements",
        };
        Assert.All(cleared, table => Assert.Equal(0, after[table]));
        Assert.Equal(32, after["schedule.teams"]);
        Assert.Equal((before["identity.users"], before["identity.sessions"], before["identity.login_tokens"]), (after["identity.users"], after["identity.sessions"], after["identity.login_tokens"]));
        // History is cleared, then the reset itself is the one entry left.
        Assert.Equal(1, after["platform.audit_entries"]);
        var entry = await Query(database => database.AuditEntries.AsNoTracking().SingleAsync());
        Assert.Equal(("development.season-data-reset", Admin), (entry.Action, entry.UserId));
        Assert.Contains("seasons", entry.Details);
    }

    [Fact]
    public async Task TheSummaryReportsWhatWasRemoved()
    {
        await SeedEverythingAsync();
        var before = await CountsAsync();

        var summary = (await Reset()).Value!;

        Assert.Equal(before["league.seasons"], summary.Removed["seasons"]);
        Assert.Equal(before["picks.ballots"], summary.Removed["ballots"]);
        Assert.Equal(before["schedule.games"], summary.Removed["games"]);
        Assert.Equal(before["platform.audit_entries"], summary.Removed["auditEntries"]);
    }

    [Fact]
    public async Task AfterResettingThereIsNoSetupSeasonAndANewOneCanBeStartedFromTheKeptPeople()
    {
        await SeedEverythingAsync();
        await Reset();

        await using var database = CreateDatabase();
        Assert.False(await database.Seasons.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
        // Starting over preserves the catalog and people, then restores only the completed reference season.
        await DevelopmentSeeder.SeedAsync(database, TestContext.Current.CancellationToken);
        Assert.Equal(2025, (await database.Seasons.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Year);
        Assert.True(await database.Users.AnyAsync(user => user.Id == Admin, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResettingTwiceIsHarmless()
    {
        await SeedEverythingAsync();
        await Reset();

        var again = await Reset();

        Assert.True(again.IsSuccess);
        Assert.Equal(0, again.Value!.Removed["seasons"]);
        Assert.Equal(1, (await CountsAsync())["platform.audit_entries"]);
    }

    [Fact]
    public async Task AFailurePartWayThroughLeavesEverythingExactlyAsItWas()
    {
        await SeedEverythingAsync();
        // Something outside the reset's list still points at a season, so deleting seasons must fail after the earlier tables have already been cleared inside the transaction.
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlRawAsync("""
                create table public.reset_probe (season uuid not null references league.seasons ("Id"));
                insert into public.reset_probe select "Id" from league.seasons limit 1
                """, cancellationToken: TestContext.Current.CancellationToken);
        }

        var before = await CountsAsync();
        var failed = await Record.ExceptionAsync(Reset);

        Assert.NotNull(failed);
        Assert.Equal(before, await CountsAsync());
    }

    private async Task<Swan.Core.Common.Result<DevelopmentResetSummary>> Reset()
    {
        await using var database = CreateDatabase();
        return await new DevelopmentResetService(database, new AuditService(database, clock)).ResetSeasonDataAsync(Admin);
    }

    private async Task<T> Query<T>(Func<SwanDbContext, Task<T>> action)
    {
        await using var database = CreateDatabase();
        return await action(database);
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
