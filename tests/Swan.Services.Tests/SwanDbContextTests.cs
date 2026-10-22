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
using Swan.Services.Platform;
using Swan.Services.Scoring;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class SwanDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public ValueTask InitializeAsync() => new(postgres.StartAsync());

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

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

        await DevelopmentSeeder.SeedAsync(database, TestContext.Current.CancellationToken);
        var firstTeamCount = await database.Teams.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var firstSeasonTeamCount = await database.SeasonTeams.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();
        await DevelopmentSeeder.SeedAsync(database, TestContext.Current.CancellationToken);

        var finishes = await database.SeasonTeams.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(32, firstTeamCount);
        Assert.Equal(32, firstSeasonTeamCount);
        Assert.Equal(32, await database.Teams.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(await database.Teams.ToListAsync(cancellationToken: TestContext.Current.CancellationToken), team => Assert.False(string.IsNullOrWhiteSpace(team.Division)));
        Assert.All(finishes.GroupBy(team => team.Division), division => Assert.Equal(new[] { 1, 2, 3, 4 }, division.Select(team => team.FinishPosition).Order()));
    }

    [Fact]
    public async Task DevelopmentSeedGivesEachCoachNineTeamsAndAnOpenDoubleDogWeekToPickFor()
    {
        await using var database = await CreateMigratedDatabase();

        await DevelopmentSeeder.SeedAsync(database, TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var rosters = await database.Rosters.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var open = await database.Weeks.AsNoTracking().SingleAsync(week => week.State == WeekState.Open, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(6, rosters.Count);
        Assert.All(rosters, roster => Assert.Equal(9, roster.Teams.Count));
        Assert.Equal((2, 16), (open.Number, open.Games.Count));
        Assert.All(open.Games, game => Assert.NotNull(game.ApprovedLine));
        Assert.NotEmpty(open.DogOptions);
        Assert.All(open.Games, game => Assert.True(game.Kickoff.Value > DateTimeOffset.UtcNow));
        Assert.Equal(WeekState.Draft, (await database.Weeks.AsNoTracking().SingleAsync(week => week.Number == 1, cancellationToken: TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task MigrationCreatesSchemasAndRoundTripsAnAggregate()
    {
        var options = new DbContextOptionsBuilder<SwanDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var database = new SwanDbContext(options);

        await database.Database.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
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
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        Assert.Equal("Coach", (await database.Users.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Name);
        Assert.Equal(1, await database.Seasons.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await database.Weeks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await database.Rosters.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await database.WeekScores.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await database.Ledgers.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await database.Announcements.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await database.ScheduledJobs.CountAsync(cancellationToken: TestContext.Current.CancellationToken));

        await database.Database.OpenConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = "select count(*) from pg_namespace where nspname in ('identity', 'league', 'schedule', 'picks', 'scoring', 'money', 'communication', 'platform')";
        Assert.Equal(8L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
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
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var restored = await database.Ballots.SingleAsync(value => value.Id == ballot.Id, cancellationToken: TestContext.Current.CancellationToken);
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
        Assert.True(week.ReplaceDogOptions([team]).IsSuccess);

        await using var database = await CreateMigratedDatabase();
        database.Weeks.Add(week);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var restored = await database.Weeks.SingleAsync(value => value.Id == week.Id, cancellationToken: TestContext.Current.CancellationToken);
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
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var restored = await database.Ledgers.SingleAsync(value => value.Id == ledger.Id, cancellationToken: TestContext.Current.CancellationToken);
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
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var restored = await database.Seasons.SingleAsync(value => value.Id == season.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(120m, restored.Settings.BuyIn.Amount);
        Assert.Equal("USD", restored.Settings.BuyIn.Currency);
        Assert.Equal(4, restored.Settings.DoubleDogWeeks);
    }

    [Fact]
    public async Task PushSubscriptionEndpointIndexIsUnique()
    {
        await using var database = await CreateMigratedDatabase();
        database.PushSubscriptions.Add(new Swan.Core.Communication.PushSubscription(new PushSubscriptionId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), "https://push.example/sub", "one", "one"));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.PushSubscriptions.Add(new Swan.Core.Communication.PushSubscription(new PushSubscriptionId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), "https://push.example/sub", "two", "two"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MagicLinkIssuesHashesRedeemsOnceAndRateLimitsRequests()
    {
        await using var database = await CreateMigratedDatabase();
        var email = Email.Create("magic@example.com").Value!;
        var user = new User(new UserId(Guid.NewGuid()), email, "Magic Coach");
        database.Users.Add(user);
        database.Seasons.Add(new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(Money.Create(100m, "USD").Value!, 3), []));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sender = new CapturingEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new MagicLinkService(database, sender, clock);
        var sent = await service.RequestAsync(email, "192.0.2.44", new Uri("https://swan.test/api/sign-in-links/redeem"), TestContext.Current.CancellationToken);
        var throttled = await service.RequestAsync(email, "192.0.2.44", new Uri("https://swan.test/api/sign-in-links/redeem"), TestContext.Current.CancellationToken);

        Assert.False(sent.IsRateLimited);
        Assert.True(throttled.IsRateLimited);
        Assert.Equal(TimeSpan.FromMinutes(1), throttled.RetryAfter);

        var rawToken = TokenFrom(sender.Messages.Single().Body);
        database.ChangeTracker.Clear();
        var stored = await database.Users.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(rawToken, stored.LoginTokens.Single().Value);

        var redeemed = await service.RedeemAsync(rawToken, TestContext.Current.CancellationToken);
        Assert.True(redeemed.IsSuccess);
        Assert.Equal("Magic Coach", redeemed.Value!.Name);
        Assert.False((await service.RedeemAsync(rawToken, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task MagicLinkRejectsExpiredInactiveAndUnknownUsersWithoutEnumeration()
    {
        await using var database = await CreateMigratedDatabase();
        var email = Email.Create("inactive@example.com").Value!;
        var user = new User(new UserId(Guid.NewGuid()), email, "Inactive Coach");
        database.Users.Add(user);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sender = new CapturingEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new MagicLinkService(database, sender, clock);
        await service.RequestAsync(email, "192.0.2.45", new Uri("https://swan.test/api/sign-in-links/redeem"), TestContext.Current.CancellationToken);
        var expired = TokenFrom(sender.Messages.Single().Body);
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        Assert.False((await service.RedeemAsync(expired, TestContext.Current.CancellationToken)).IsSuccess);

        database.ChangeTracker.Clear();
        var inactive = await database.Users.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        inactive.Deactivate();
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        await service.RequestAsync(email, "192.0.2.46", new Uri("https://swan.test/api/sign-in-links/redeem"), TestContext.Current.CancellationToken);
        await service.RequestAsync(Email.Create("unknown@example.com").Value!, "192.0.2.47", new Uri("https://swan.test/api/sign-in-links/redeem"), TestContext.Current.CancellationToken);

        Assert.Single(sender.Messages);
        Assert.False((await service.RedeemAsync(expired, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task ASessionAuthenticatesTheUserItWasIssuedToUntilItExpiresIsRevokedOrTheUserIsDeactivated()
    {
        await using var database = await CreateMigratedDatabase();
        var start = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var user = new User(new UserId(Guid.NewGuid()), Email.Create("session@example.com").Value!, "Session Coach");
        database.Users.Add(user);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sessions = new SessionService(database, new FakeClock(start));
        var issued = await sessions.CreateAsync(user.Id, TestContext.Current.CancellationToken);

        var current = await sessions.AuthenticateAsync(issued.Credential, TestContext.Current.CancellationToken);

        Assert.Equal(("Session Coach", "Coach", user.Id), (current!.Name, current.Role, current.UserId));
        Assert.Equal(start.Add(SessionService.Lifetime), issued.ExpiresAt);
        Assert.Matches("^[0-9A-F]{64}$", issued.Credential);
        Assert.NotEqual(issued.Credential, (await database.Sessions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).TokenHash);
        // A user id, a made-up credential, a lower-case copy and nothing at all are all refused.
        Assert.Null(await sessions.AuthenticateAsync(user.Id.Value.ToString(), TestContext.Current.CancellationToken));
        Assert.Null(await sessions.AuthenticateAsync(Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()), TestContext.Current.CancellationToken));
        Assert.Null(await sessions.AuthenticateAsync(issued.Credential.ToLowerInvariant(), TestContext.Current.CancellationToken));
        Assert.Null(await sessions.AuthenticateAsync(null, TestContext.Current.CancellationToken));
        Assert.Null(await new SessionService(database, new FakeClock(issued.ExpiresAt)).AuthenticateAsync(issued.Credential, TestContext.Current.CancellationToken));
        Assert.NotNull(await new SessionService(database, new FakeClock(issued.ExpiresAt.AddTicks(-1))).AuthenticateAsync(issued.Credential, TestContext.Current.CancellationToken));

        await sessions.RevokeAsync(issued.Credential, TestContext.Current.CancellationToken);
        Assert.Null(await sessions.AuthenticateAsync(issued.Credential, TestContext.Current.CancellationToken));
        var second = await sessions.CreateAsync(user.Id, TestContext.Current.CancellationToken);
        Assert.NotEqual(issued.Credential, second.Credential);
        user.Deactivate();
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Null(await sessions.AuthenticateAsync(second.Credential, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuditServiceAppendsImmutableEntriesAndFiltersThem()
    {
        await using var database = await CreateMigratedDatabase();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var actor = new UserId(Guid.NewGuid());
        var audit = new AuditService(database, clock);

        await audit.AppendAsync(actor, "season.activate", "season-1", new { state = "setup" }, new { state = "live" }, "Commissioner approved", TestContext.Current.CancellationToken);
        await audit.AppendAsync(new UserId(Guid.NewGuid()), "season.activate", "season-2", cancellationToken: TestContext.Current.CancellationToken);

        var entries = await audit.QueryAsync(new AuditFilter(Actor: actor, Action: "season.activate", From: clock.UtcNow.AddMinutes(-1), To: clock.UtcNow.AddMinutes(1)), TestContext.Current.CancellationToken);
        var entry = Assert.Single(entries);
        Assert.Equal(actor, entry.UserId);
        Assert.Contains("Commissioner approved", entry.Details);
        Assert.Contains("setup", entry.Details);
        Assert.Contains("live", entry.Details);
    }

    [Fact]
    public async Task StandingsRankStoredScoreLinesAndSeparateDogPoints()
    {
        await using var database = await CreateMigratedDatabase();
        var season = new SeasonId(Guid.NewGuid());
        var first = new MembershipId(Guid.NewGuid());
        var second = new MembershipId(Guid.NewGuid());
        var week = new WeekId(Guid.NewGuid());
        database.Weeks.Add(new Week(week, season, 1, new WeekType(WeekKind.Regular, null, true)));
        var firstScore = new WeekScore(new WeekScoreId(Guid.NewGuid()), season, first, week);
        firstScore.ReplaceLines([new Swan.Core.Scoring.ScoreLine(new TeamId(Guid.NewGuid()), 4, Swan.Core.Scoring.ScoreSource.TeamPick, "game-1"), new Swan.Core.Scoring.ScoreLine(new TeamId(Guid.NewGuid()), 3, Swan.Core.Scoring.ScoreSource.DogPick, "game-2")]);
        var secondScore = new WeekScore(new WeekScoreId(Guid.NewGuid()), season, second, week);
        secondScore.ReplaceLines([new Swan.Core.Scoring.ScoreLine(new TeamId(Guid.NewGuid()), 5, Swan.Core.Scoring.ScoreSource.TeamPick, "game-3")]);
        database.WeekScores.AddRange(firstScore, secondScore);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var standings = new StandingsService(database);
        var main = await standings.GetAsync(season, StandingPool.Main, cancellationToken: TestContext.Current.CancellationToken);
        var dogs = await standings.GetAsync(season, StandingPool.Dog, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(second, main[0].MembershipId);
        Assert.Equal(5, main[0].Points);
        Assert.Equal(first, dogs[0].MembershipId);
        Assert.Equal(3, dogs[0].Points);
    }

    [Fact]
    public async Task StandingsSeparateRegularPlayoffAndDogScores()
    {
        await using var database = await CreateMigratedDatabase();
        var season = new SeasonId(Guid.NewGuid());
        var membership = new MembershipId(Guid.NewGuid());
        var regularWeek = new WeekId(Guid.NewGuid());
        var playoffWeek = new WeekId(Guid.NewGuid());
        database.Weeks.AddRange(
            new Week(regularWeek, season, 1, new WeekType(WeekKind.Regular, null, true)),
            new Week(playoffWeek, season, 19, new WeekType(WeekKind.Playoff, "Wild card", true)));

        var regular = new WeekScore(new WeekScoreId(Guid.NewGuid()), season, membership, regularWeek);
        regular.ReplaceLines([
            new ScoreLine(new TeamId(Guid.NewGuid()), 4, ScoreSource.TeamPick, "regular-win"),
            new ScoreLine(new TeamId(Guid.NewGuid()), 2, ScoreSource.DogPick, "regular-dog")]);
        var playoff = new WeekScore(new WeekScoreId(Guid.NewGuid()), season, membership, playoffWeek);
        playoff.ReplaceLines([
            new ScoreLine(new TeamId(Guid.NewGuid()), 6, ScoreSource.TeamPick, "playoff-win"),
            new ScoreLine(null, 3, ScoreSource.PlayoffQualification, "qualification")]);
        database.WeekScores.AddRange(regular, playoff);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var standings = new StandingsService(database);
        var main = Assert.Single(await standings.GetAsync(season, StandingPool.Main, cancellationToken: TestContext.Current.CancellationToken));
        var dogPool = Assert.Single(await standings.GetAsync(season, StandingPool.Dog, cancellationToken: TestContext.Current.CancellationToken));
        var regularSeason = Assert.Single(await standings.GetAsync(season, StandingPool.RegularSeason, cancellationToken: TestContext.Current.CancellationToken));
        var playoffPool = Assert.Single(await standings.GetAsync(season, StandingPool.Playoff, cancellationToken: TestContext.Current.CancellationToken));
        var currentWeek = Assert.Single(await standings.GetAsync(season, StandingPool.Main, weekId: playoffWeek, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(13, main.Points);
        Assert.Equal(2, dogPool.Points);
        Assert.Equal(4, regularSeason.Points);
        Assert.Equal(9, playoffPool.Points);
        Assert.Equal(9, currentWeek.Points);
        Assert.True(main.TiebreakUnbroken);
    }

    [Fact]
    public async Task StandingsAssignSharedUnbrokenRankToTies()
    {
        await using var database = await CreateMigratedDatabase();
        var season = new SeasonId(Guid.NewGuid());
        var week = new WeekId(Guid.NewGuid());
        database.Weeks.Add(new Week(week, season, 1, new WeekType(WeekKind.Regular, null, true)));
        var scores = Enumerable.Range(0, 2).Select(_ =>
        {
            var score = new WeekScore(new WeekScoreId(Guid.NewGuid()), season, new MembershipId(Guid.NewGuid()), week);
            score.ReplaceLines([new ScoreLine(new TeamId(Guid.NewGuid()), 4, ScoreSource.TeamPick, "win")]);
            return score;
        });
        database.WeekScores.AddRange(scores);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var standings = await new StandingsService(database).GetAsync(season, StandingPool.Main, cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(standings, standing =>
        {
            Assert.Equal(1, standing.Rank);
            Assert.True(standing.TiebreakUnbroken);
        });
    }

    [Fact]
    public async Task StandingsUseCompetitionRankingSoTiesShareARankAndSkipTheNext()
    {
        await using var database = await CreateMigratedDatabase();
        var season = new SeasonId(Guid.NewGuid());
        var week = new WeekId(Guid.NewGuid());
        database.Weeks.Add(new Week(week, season, 1, new WeekType(WeekKind.Regular, null, true)));
        var scores = new[] { 4, 4, 4, 2 }.Select(points =>
        {
            var score = new WeekScore(new WeekScoreId(Guid.NewGuid()), season, new MembershipId(Guid.NewGuid()), week);
            score.ReplaceLines([new ScoreLine(new TeamId(Guid.NewGuid()), points, ScoreSource.TeamPick, "win")]);
            return score;
        });
        database.WeekScores.AddRange(scores);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var standings = await new StandingsService(database).GetAsync(season, StandingPool.Main, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 1, 4], standings.Select(standing => standing.Rank));
        Assert.Equal([4, 4, 4, 2], standings.Select(standing => standing.Points));
        Assert.All(standings.Take(3), standing => Assert.True(standing.TiebreakUnbroken));
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

    [Fact]
    public async Task GameLineHistoryRoundTripsInOrderAndLatestLineCanBeApprovedAfterReload()
    {
        await using var database = await CreateMigratedDatabase();
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        var gameId = new GameId(Guid.NewGuid());
        week.AddGame(new Game(gameId, new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow.AddDays(3))));
        foreach (var spread in new[] { -3.5m, 2m, -7m }) Assert.True(week.AddLine(gameId, new Spread(spread)).IsSuccess);
        database.Weeks.Add(week);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        var reloaded = await database.Weeks.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([-3.5m, 2m, -7m], reloaded.Games.Single().LineHistory.Select(line => line.Value));
        Assert.True(reloaded.ApproveLatestLine(gameId).IsSuccess);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();
        var approved = (await database.Weeks.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Games.Single();
        Assert.Equal(-7m, approved.ApprovedLine!.Value);
        Assert.Equal(3, approved.LineHistory.Count);
    }

    private async Task<SwanDbContext> CreateMigratedDatabase()
    {
        var options = new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var database = new SwanDbContext(options);
        await database.Database.MigrateAsync();
        return database;
    }
}
