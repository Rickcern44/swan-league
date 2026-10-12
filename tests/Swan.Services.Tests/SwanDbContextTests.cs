using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Finance;
using Swan.Core.Identity;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Platform;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
using Swan.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class SwanDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

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
        database.Teams.Add(new Team(new TeamId(Guid.NewGuid()), "Swans", null, DateTimeOffset.UtcNow));
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
}
