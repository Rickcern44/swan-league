using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Data;
using Swan.Services.League;
using Swan.Services.Platform;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class SeasonSetupServiceTests : IAsyncLifetime
{
    private static readonly UserId Admin = new(Guid.Parse("9c9b2c10-a312-4b77-a26a-ef546e87f3c9"));
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SeasonIdentityPoolsAndSetupStateRoundTripThroughPostgres()
    {
        await Migrate();
        var id = new SeasonId(Guid.NewGuid());
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(Season.CreateBlank(id, 2026, "2026 Season", isSimulation: true).Value!);
            await database.SaveChangesAsync();
        }

        await using var reloaded = CreateDatabase();
        var season = await reloaded.Seasons.SingleAsync(value => value.Id == id);
        Assert.Equal((2026, "2026 Season", true, SeasonState.Setup), (season.Year, season.Name, season.IsSimulation, season.State));
        Assert.Equal([("Main", 60m), ("Dog", 20m), ("Regular Season", 10m), ("Playoff", 10m)], season.Pools.Select(pool => (pool.Name, pool.Stake.Amount)));
        Assert.Equal(new TimeOnly(13, 0), season.Settings.TeamPickDeadlineEastern);
        Assert.True(season.Activate().IsSuccess);
    }

    [Fact]
    public async Task RowsWrittenBeforeTheColumnsExistedReadWithSafeDefaults()
    {
        await Migrate();
        var id = Guid.NewGuid();
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({id}, '100|USD|3|', 1)""");
        }

        await using var reloaded = CreateDatabase();
        var legacy = await reloaded.Seasons.SingleAsync(value => value.Id == new SeasonId(id));
        Assert.Equal((0, "", false, SeasonState.Live), (legacy.Year, legacy.Name, legacy.IsSimulation, legacy.State));
        Assert.Empty(legacy.Pools);
    }

    [Fact]
    public async Task CreateBlankSeasonPersistsAuditsAndIsFoundAsTheOnlySetupSeason()
    {
        await Migrate();

        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var found = await Run(service => service.GetSetupAsync());

        Assert.True(created.IsSuccess);
        Assert.Equal(created.Value!.Id, found.Value!.Id);
        Assert.Equal((2026, "2026 Season", false, SeasonState.Setup), (found.Value.Year, found.Value.Name, found.Value.IsSimulation, found.Value.State));
        Assert.Equal(4, found.Value.Pools.Count);
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync());
        Assert.Equal(("season.setup.created", Admin, created.Value.Id.Value.ToString()), (audit.Action, audit.UserId, audit.ResourceId));
        Assert.Contains("Main", audit.Details);
    }

    [Fact]
    public async Task SecondSetupSeasonIsRejectedAndALiveSeasonIsLeftAlone()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.Activate().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        var noSetupYet = await Run(service => service.GetSetupAsync());
        var first = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var second = await Run(service => service.CreateBlankAsync(Admin, 2027, "2027 Season", isSimulation: false));

        Assert.Equal("season.missing", noSetupYet.Error!.Code);
        Assert.True(first.IsSuccess);
        Assert.Equal("season.setup.exists", second.Error!.Code);
        Assert.Equal([SeasonState.Live, SeasonState.Setup], await Run((service, database) => database.Seasons.OrderBy(season => season.Year).Select(season => season.State).ToListAsync()));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Theory]
    [InlineData("", 2026, "season.name")]
    [InlineData("   ", 2026, "season.name")]
    [InlineData("2026", 1999, "season.year")]
    public async Task InvalidInputIsRejectedWithoutPersistingOrAuditing(string name, int year, string code)
    {
        await Migrate();

        var result = await Run(service => service.CreateBlankAsync(Admin, year, name, isSimulation: false));

        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(0, await Run((service, database) => database.Seasons.CountAsync()));
        Assert.Equal(0, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task PostgresRejectsASecondSetupSeasonButAllowsManyLiveOnes()
    {
        await Migrate();
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", isSimulation: false).Value!);
            await database.SaveChangesAsync();
        }

        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2027, "2027 Season", isSimulation: false).Value!);
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            Assert.IsType<Npgsql.PostgresException>(rejected.InnerException);
        }

        await using (var database = CreateDatabase())
        {
            foreach (var year in new[] { 2024, 2025 })
            {
                var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), year, $"{year} Season", isSimulation: false).Value!;
                Assert.True(live.Activate().IsSuccess);
                database.Seasons.Add(live);
            }

            await database.SaveChangesAsync();
            Assert.Equal(3, await database.Seasons.CountAsync());
        }
    }

    [Fact]
    public async Task ConcurrentCreatesYieldExactlyOneSetupSeasonAndOneAuditRow()
    {
        await Migrate();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Run(service => service.CreateBlankAsync(Admin, 2026 + index, $"Season {index}", isSimulation: false))));

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.All(results.Where(result => !result.IsSuccess), result => Assert.Equal("season.setup.exists", result.Error!.Code));
        Assert.Equal(1, await Run((service, database) => database.Seasons.CountAsync(season => season.State == SeasonState.Setup)));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task MigrationRefusesToRunOverExistingDuplicateSetupSeasonsAndLeavesThemUntouched()
    {
        await using (var database = CreateDatabase())
        {
            await database.GetService<IMigrator>().MigrateAsync("20261013132526_AddSeasonIdentityAndPools");
            foreach (var id in new[] { Guid.NewGuid(), Guid.NewGuid() })
                await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({id}, '100|USD|3|', 0)""");
        }

        await using var upgrading = CreateDatabase();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => upgrading.Database.MigrateAsync());

        Assert.Contains("more than one Setup season", failure.ToString());
        Assert.Equal(2, await upgrading.Seasons.CountAsync(season => season.State == SeasonState.Setup));
    }

    [Fact]
    public async Task EditingSetupBasicsPersistsAuditsOldAndNewValuesAndKeepsPoolsAndSettings()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var updated = await Run(service => service.UpdateSetupBasicsAsync(Admin, 2027, "  2027 Season  ", isSimulation: true));
        var reloaded = await Run(service => service.GetSetupAsync());

        Assert.True(updated.IsSuccess);
        Assert.Equal(created.Value!.Id, reloaded.Value!.Id);
        Assert.Equal((2027, "2027 Season", true, SeasonState.Setup), (reloaded.Value.Year, reloaded.Value.Name, reloaded.Value.IsSimulation, reloaded.Value.State));
        Assert.Equal(created.Value.Settings, reloaded.Value.Settings);
        Assert.Equal(created.Value.Pools, reloaded.Value.Pools);
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.updated"));
        Assert.Equal((Admin, created.Value.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("2026 Season", audit.Details);
        Assert.Contains("2027 Season", audit.Details);
    }

    [Fact]
    public async Task RepeatingAnAppliedEditSucceedsWithoutAppendingASecondAuditEntry()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        // The second call sees exactly what an execution-strategy retry sees after a commit whose acknowledgement was lost.
        var first = await Run(service => service.UpdateSetupBasicsAsync(Admin, 2027, "2027 Season", isSimulation: true));
        var replay = await Run(service => service.UpdateSetupBasicsAsync(Admin, 2027, "2027 Season", isSimulation: true));

        Assert.True(first.IsSuccess);
        Assert.Equal((2027, "2027 Season", true), (replay.Value!.Year, replay.Value.Name, replay.Value.IsSimulation));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.updated")));
    }

    [Theory]
    [InlineData("", 2027, "season.name")]
    [InlineData("2027", 2101, "season.year")]
    public async Task InvalidEditsAreRejectedWithoutChangingOrAuditing(string name, int year, string code)
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var result = await Run(service => service.UpdateSetupBasicsAsync(Admin, year, name, isSimulation: true));

        Assert.Equal(code, result.Error!.Code);
        var found = await Run(service => service.GetSetupAsync());
        Assert.Equal((2026, "2026 Season", false), (found.Value!.Year, found.Value.Name, found.Value.IsSimulation));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task EditingWithNoSetupSeasonIsMissingAndALiveSeasonIsNeverChanged()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.Activate().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        var result = await Run(service => service.UpdateSetupBasicsAsync(Admin, 2030, "Changed", isSimulation: true));

        Assert.Equal("season.missing", result.Error!.Code);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal((2025, "2025 Season", false, SeasonState.Live), (stored.Year, stored.Name, stored.IsSimulation, stored.State));
        Assert.Equal(0, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task EditWaitsForAnInFlightActivationAndThenLeavesTheLiveSeasonUntouched()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        await using var activating = CreateDatabase();
        await using var transaction = await activating.Database.BeginTransactionAsync();
        await activating.Database.ExecuteSqlRawAsync("""update league.seasons set "State" = 1""");

        var edit = Run(service => service.UpdateSetupBasicsAsync(Admin, 2030, "Changed", isSimulation: true));
        await Task.Delay(500);
        Assert.False(edit.IsCompleted);
        await transaction.CommitAsync();
        var result = await edit;

        Assert.False(result.IsSuccess);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal((2026, "2026 Season", false, SeasonState.Live), (stored.Year, stored.Name, stored.IsSimulation, stored.State));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    private static readonly SetupMoney NewMoney = new(new Money(150m, "USD"), new Money(90m, "USD"), new Money(30m, "USD"), new Money(15m, "USD"), new Money(15m, "USD"));

    [Fact]
    public async Task EditingSetupMoneyPersistsAuditsOldAndNewAndKeepsEverythingElse()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: true));

        var updated = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));
        var reloaded = await Run(service => service.GetSetupAsync());

        Assert.True(updated.IsSuccess);
        Assert.Equal(new Money(150m, "USD"), reloaded.Value!.Settings.BuyIn);
        Assert.Equal([("Main", 90m), ("Dog", 30m), ("Regular Season", 15m), ("Playoff", 15m)], reloaded.Value.Pools.Select(pool => (pool.Name, pool.Stake.Amount)));
        Assert.Equal((created.Value!.Id, 2026, "2026 Season", true, SeasonState.Setup), (reloaded.Value.Id, reloaded.Value.Year, reloaded.Value.Name, reloaded.Value.IsSimulation, reloaded.Value.State));
        Assert.Equal((created.Value.Settings.DoubleDogWeeks, created.Value.Settings.TeamPickDeadlineEastern), (reloaded.Value.Settings.DoubleDogWeeks, reloaded.Value.Settings.TeamPickDeadlineEastern));
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.money.updated"));
        Assert.Equal((Admin, created.Value.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("100", audit.Details);
        Assert.Contains("150", audit.Details);
    }

    [Fact]
    public async Task InvalidMoneyIsRejectedWithoutChangingOrAuditing()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var result = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney with { Playoff = new Money(10m, "USD") }));

        Assert.Equal("season.money", result.Error!.Code);
        var found = await Run(service => service.GetSetupAsync());
        Assert.Equal(new Money(100m, "USD"), found.Value!.Settings.BuyIn);
        Assert.Equal(60m, found.Value.Pools.Single(pool => pool.Name == "Main").Stake.Amount);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task RepeatingAnAppliedMoneyEditSucceedsWithoutASecondAuditEntry()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var first = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));
        var replay = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));
        var unchangedDefaults = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));

        Assert.True(first.IsSuccess && replay.IsSuccess && unchangedDefaults.IsSuccess);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.money.updated")));
    }

    [Fact]
    public async Task MoneyEditWithNoSetupSeasonIsMissingAndALiveSeasonIsNeverChanged()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.Activate().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        var result = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));

        Assert.Equal("season.missing", result.Error!.Code);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal(new Money(100m, "USD"), stored.Settings.BuyIn);
        Assert.Equal(60m, stored.Pools.Single(pool => pool.Name == "Main").Stake.Amount);
        Assert.Equal(0, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task MoneyEditWaitsForAnInFlightActivationAndThenLeavesTheLiveSeasonUntouched()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        await using var activating = CreateDatabase();
        await using var transaction = await activating.Database.BeginTransactionAsync();
        await activating.Database.ExecuteSqlRawAsync("""update league.seasons set "State" = 1""");

        var edit = Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));
        await Task.Delay(500);
        Assert.False(edit.IsCompleted);
        await transaction.CommitAsync();
        var result = await edit;

        Assert.False(result.IsSuccess);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal((new Money(100m, "USD"), SeasonState.Live), (stored.Settings.BuyIn, stored.State));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task LegacySetupSeasonWithoutPoolsCanHaveItsMoneySet()
    {
        await Migrate();
        var id = Guid.NewGuid();
        await using (var database = CreateDatabase())
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({id}, '100|USD|3|', 0)""");

        var updated = await Run(service => service.UpdateSetupMoneyAsync(Admin, NewMoney));

        Assert.True(updated.IsSuccess);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal([("Main", 90m), ("Dog", 30m), ("Regular Season", 15m), ("Playoff", 15m)], stored.Pools.Select(pool => (pool.Name, pool.Stake.Amount)));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.money.updated")));
    }

    private async Task Migrate()
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
    }

    private Task<T> Run<T>(Func<SeasonSetupService, Task<T>> action) => Run((service, _) => action(service));

    private async Task<T> Run<T>(Func<SeasonSetupService, SwanDbContext, Task<T>> action)
    {
        await using var database = CreateDatabase();
        return await action(new SeasonSetupService(database, new AuditService(database, new SystemClock())), database);
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure()).Options);
}
