using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Finance;
using Swan.Core.Identity;
using Swan.Core.Integrations;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Platform;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
using Swan.Data;
using Swan.Data.Seed;
using Swan.Services.Identity;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class SwanDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public void DesignTimeFactoryUsesNpgsqlWithoutConnecting()
    {
        using var database = new SwanDbContextFactory().CreateDbContext(["--connection", "Host=example.invalid;Database=swan"]);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", database.Database.ProviderName);
    }

    [Fact]
    public async Task DevelopmentSeedIsIdempotentAndProvidesDivisionFinishes()
    {
        await using var database = await CreateMigratedDatabase();

        await DevelopmentSeeder.SeedAsync(database);
        var firstTeamCount = await database.Teams.CountAsync();
        var firstSeasonTeamCount = await database.SeasonTeams.CountAsync();
        database.ChangeTracker.Clear();
        await DevelopmentSeeder.SeedAsync(database);

        var finishes = await database.SeasonTeams.ToListAsync();
        Assert.Equal(32, firstTeamCount);
        Assert.Equal(32, firstSeasonTeamCount);
        Assert.Equal(32, await database.Teams.CountAsync());
        Assert.All(await database.Teams.ToListAsync(), team => Assert.False(string.IsNullOrWhiteSpace(team.Division)));
        Assert.All(finishes.GroupBy(team => team.Division), division => Assert.Equal(new[] { 1, 2, 3, 4 }, division.Select(team => team.FinishPosition).Order()));
    }

    [Fact]
    public async Task MigrationCreatesSchemasAndRoundTripsAnAggregate()
    {
        var options = new DbContextOptionsBuilder<SwanDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var database = new SwanDbContext(options);

        await database.Database.MigrateAsync();
        var seasonId = new SeasonId(Guid.NewGuid());
        database.Users.Add(new User(new UserId(Guid.NewGuid()), Email.Create("coach@example.com").Value!, "Coach"));
        database.Seasons.Add(new Season(seasonId, new SeasonSettings(Money.Create(100m, "USD").Value!, 3), []));
        database.Teams.Add(new Team(new TeamId(Guid.NewGuid()), "Swans", null, DateTimeOffset.UtcNow, "East"));
        database.Weeks.Add(new Week(new WeekId(Guid.NewGuid()), seasonId, 1, new WeekType(WeekKind.Regular, null, true)));
        database.Rosters.Add(new Roster(new RosterId(Guid.NewGuid()), seasonId, new MembershipId(Guid.NewGuid())));
        database.WeekScores.Add(new WeekScore(new WeekScoreId(Guid.NewGuid()), seasonId, new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid())));
        database.Ledgers.Add(new Ledger(new LedgerId(Guid.NewGuid()), seasonId));
        database.Announcements.Add(new Swan.Core.Communication.Announcement(new AnnouncementId(Guid.NewGuid()), "Welcome", DateTimeOffset.UtcNow));
        database.ScheduledJobs.Add(new ScheduledJob(new ScheduledJobId(Guid.NewGuid()), "send-reminders", DateTimeOffset.UtcNow));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        Assert.Equal("Coach", (await database.Users.SingleAsync()).Name);
        Assert.Equal(1, await database.Seasons.CountAsync());
        Assert.Equal(1, await database.Weeks.CountAsync());
        Assert.Equal(1, await database.Rosters.CountAsync());
        Assert.Equal(1, await database.WeekScores.CountAsync());
        Assert.Equal(1, await database.Ledgers.CountAsync());
        Assert.Equal(1, await database.Announcements.CountAsync());
        Assert.Equal(1, await database.ScheduledJobs.CountAsync());

        await database.Database.OpenConnectionAsync();
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = "select count(*) from pg_namespace where nspname in ('identity', 'league', 'schedule', 'picks', 'scoring', 'money', 'communication', 'platform')";
        Assert.Equal(8L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task BallotPicksDogsAndChangeHistoryRoundTrip()
    {
        var now = DateTimeOffset.UtcNow;
        var teams = new[] { new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()) };
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()), teams);
        var first = new TeamPick(teams[0], new GameId(Guid.NewGuid()), now.AddHours(1));
        ballot.AddPick(first, now);
        ballot.ChangePick(teams[0], new TeamPick(teams[1], new GameId(Guid.NewGuid()), now.AddHours(1)), now, "injury");
        ballot.AddDog(new DogPick(teams[0], new GameId(Guid.NewGuid()), now.AddHours(1)), false, now);

        await using var database = await CreateMigratedDatabase();
        database.Ballots.Add(ballot);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var restored = await database.Ballots.SingleAsync(value => value.Id == ballot.Id);
        Assert.Single(restored.Picks);
        Assert.Single(restored.Dogs);
        Assert.Single(restored.Changes);
        Assert.Equal("injury", restored.Changes.Single().Reason);
    }

    [Fact]
    public async Task WeekGamesApprovedLinesAndDogOptionsRoundTrip()
    {
        var team = new TeamId(Guid.NewGuid());
        var gameId = new GameId(Guid.NewGuid());
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        week.AddGame(new Game(gameId, team, new TeamId(Guid.NewGuid()), Kickoff.Create(DateTimeOffset.UtcNow).Value!));
        week.AddLine(gameId, Spread.Create(-3.5m).Value!);
        week.ApproveLatestLine(gameId);
        week.SetDogOptions([new DogOption(team)]);

        await using var database = await CreateMigratedDatabase();
        database.Weeks.Add(week);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var restored = await database.Weeks.SingleAsync(value => value.Id == week.Id);
        Assert.Equal(-3.5m, restored.Games.Single().ApprovedLine!.Value);
        Assert.Equal(team, restored.DogOptions.Single().TeamId);
    }

    [Fact]
    public async Task LedgerItemsRoundTrip()
    {
        var ledger = new Ledger(new LedgerId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()));
        var amount = Money.Create(25m, "USD").Value!;
        ledger.AssessFine(amount, "late", new MembershipId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()));
        ledger.AddTransfer(Money.Create(50m, "USD").Value!, "payout", new MembershipId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()));

        await using var database = await CreateMigratedDatabase();
        database.Ledgers.Add(ledger);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var restored = await database.Ledgers.SingleAsync(value => value.Id == ledger.Id);
        Assert.Equal(new[] { 25m, 50m }, restored.Items.Select(item => item.Amount.Amount).Order());
        Assert.Contains(restored.Items, item => item.Memo == "payout");
    }

    [Fact]
    public async Task SeasonSettingsValueObjectsRoundTrip()
    {
        var settings = new SeasonSettings(Money.Create(120m, "USD").Value!, 4);
        var season = new Season(new SeasonId(Guid.NewGuid()), settings, []);

        await using var database = await CreateMigratedDatabase();
        database.Seasons.Add(season);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var restored = await database.Seasons.SingleAsync(value => value.Id == season.Id);
        Assert.Equal(120m, restored.Settings.BuyIn.Amount);
        Assert.Equal("USD", restored.Settings.BuyIn.Currency);
        Assert.Equal(4, restored.Settings.DoubleDogWeeks);
    }

    [Fact]
    public async Task PushSubscriptionEndpointIndexIsUnique()
    {
        await using var database = await CreateMigratedDatabase();
        database.PushSubscriptions.Add(new Swan.Core.Communication.PushSubscription(new PushSubscriptionId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), "https://push.example/sub", "one", "one"));
        await database.SaveChangesAsync();
        database.PushSubscriptions.Add(new Swan.Core.Communication.PushSubscription(new PushSubscriptionId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), "https://push.example/sub", "two", "two"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task MagicLinkIssuesHashesRedeemsOnceAndRateLimitsRequests()
    {
        await using var database = await CreateMigratedDatabase();
        var email = Email.Create("magic@example.com").Value!;
        var user = new User(new UserId(Guid.NewGuid()), email, "Magic Coach");
        database.Users.Add(user);
        database.Seasons.Add(new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(Money.Create(100m, "USD").Value!, 3), []));
        await database.SaveChangesAsync();

        var sender = new CapturingEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new MagicLinkService(database, sender, clock);
        await service.RequestAsync(email, "192.0.2.44", new Uri("https://swan.test/api/sign-in-links/redeem"));
        await service.RequestAsync(email, "192.0.2.44", new Uri("https://swan.test/api/sign-in-links/redeem"));

        var rawToken = TokenFrom(sender.Messages.Single().Body);
        database.ChangeTracker.Clear();
        var stored = await database.Users.SingleAsync();
        Assert.NotEqual(rawToken, stored.LoginTokens.Single().Value);

        var redeemed = await service.RedeemAsync(rawToken);
        Assert.True(redeemed.IsSuccess);
        Assert.Equal("Magic Coach", redeemed.Value!.Name);
        Assert.False((await service.RedeemAsync(rawToken)).IsSuccess);
    }

    [Fact]
    public async Task MagicLinkRejectsExpiredInactiveAndUnknownUsersWithoutEnumeration()
    {
        await using var database = await CreateMigratedDatabase();
        var email = Email.Create("inactive@example.com").Value!;
        var user = new User(new UserId(Guid.NewGuid()), email, "Inactive Coach");
        database.Users.Add(user);
        await database.SaveChangesAsync();

        var sender = new CapturingEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new MagicLinkService(database, sender, clock);
        await service.RequestAsync(email, "192.0.2.45", new Uri("https://swan.test/api/sign-in-links/redeem"));
        var expired = TokenFrom(sender.Messages.Single().Body);
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        Assert.False((await service.RedeemAsync(expired)).IsSuccess);

        database.ChangeTracker.Clear();
        var inactive = await database.Users.SingleAsync();
        inactive.Deactivate();
        await database.SaveChangesAsync();
        await service.RequestAsync(email, "192.0.2.46", new Uri("https://swan.test/api/sign-in-links/redeem"));
        await service.RequestAsync(Email.Create("unknown@example.com").Value!, "192.0.2.47", new Uri("https://swan.test/api/sign-in-links/redeem"));

        Assert.Single(sender.Messages);
        Assert.False((await service.RedeemAsync(expired)).IsSuccess);
    }

    [Fact]
    public async Task SessionReturnsActiveUserAndMembershipRole()
    {
        await using var database = await CreateMigratedDatabase();
        var user = new User(new UserId(Guid.NewGuid()), Email.Create("session@example.com").Value!, "Session Coach");
        database.Users.Add(user);
        database.Memberships.Add(new Membership(new MembershipId(Guid.NewGuid()), user.Id, new SeasonId(Guid.NewGuid()), MembershipRole.Coach));
        await database.SaveChangesAsync();

        var current = await new SessionService(database).GetAsync(user.Id);

        Assert.NotNull(current);
        Assert.Equal("Session Coach", current.Name);
        Assert.Equal("Coach", current.Role);
        Assert.Null(await new SessionService(database).GetAsync(new UserId(Guid.NewGuid())));
    }

    private static string TokenFrom(string body) => new Uri(body).Query.Split('=', 2)[1];

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private async Task<SwanDbContext> CreateMigratedDatabase()
    {
        var options = new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var database = new SwanDbContext(options);
        await database.Database.MigrateAsync();
        return database;
    }
}
