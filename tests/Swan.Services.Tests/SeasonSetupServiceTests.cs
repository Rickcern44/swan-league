using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Integrations;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
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
        Assert.True(season.ActivateWithDogs().IsSuccess);
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
        Assert.True(live.ActivateWithDogs().IsSuccess);
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
                Assert.True(live.ActivateWithDogs().IsSuccess);
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
        Assert.True(live.ActivateWithDogs().IsSuccess);
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
        Assert.True(live.ActivateWithDogs().IsSuccess);
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

    [Fact]
    public async Task EditingTheSetupDeadlinePersistsAuditsBeforeAndAfterAndKeepsEverythingElse()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: true));

        var updated = await Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)));
        var reloaded = await Run(service => service.GetSetupAsync());

        Assert.True(updated.IsSuccess);
        Assert.Equal(new TimeOnly(11, 30), reloaded.Value!.Settings.TeamPickDeadlineEastern);
        Assert.Equal((created.Value!.Id, 2026, "2026 Season", true, SeasonState.Setup), (reloaded.Value.Id, reloaded.Value.Year, reloaded.Value.Name, reloaded.Value.IsSimulation, reloaded.Value.State));
        Assert.Equal(created.Value.Settings with { TeamPickDeadlineEastern = new TimeOnly(11, 30) }, reloaded.Value.Settings);
        Assert.Equal(created.Value.Pools, reloaded.Value.Pools);
        var weekStart = new DateTimeOffset(2025, 10, 6, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2025, 10, 12, 15, 30, 0, TimeSpan.Zero), LockPolicy.WeekDeadline(reloaded.Value.Settings, weekStart));
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.deadline.updated"));
        Assert.Equal((Admin, created.Value.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("13:00", audit.Details);
        Assert.Contains("11:30", audit.Details);
    }

    [Fact]
    public async Task RepeatingAnAppliedDeadlineEditSucceedsWithoutASecondAuditEntry()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var first = await Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)));
        var replay = await Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)));
        var secondReplay = await Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)));

        Assert.True(first.IsSuccess && replay.IsSuccess && secondReplay.IsSuccess);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.deadline.updated")));
    }

    [Fact]
    public async Task DeadlineEditWithNoSetupSeasonIsMissingAndALiveSeasonIsNeverChanged()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.ActivateWithDogs().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        var result = await Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)));

        Assert.Equal("season.missing", result.Error!.Code);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal(new TimeOnly(13, 0), stored.Settings.TeamPickDeadlineEastern);
        Assert.Equal(0, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task DeadlineEditWaitsForAnInFlightActivationAndThenLeavesTheLiveSeasonUntouched()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        await using var activating = CreateDatabase();
        await using var transaction = await activating.Database.BeginTransactionAsync();
        await activating.Database.ExecuteSqlRawAsync("""update league.seasons set "State" = 1""");

        var edit = Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)));
        await Task.Delay(500);
        Assert.False(edit.IsCompleted);
        await transaction.CommitAsync();
        var result = await edit;

        Assert.False(result.IsSuccess);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal((new TimeOnly(13, 0), SeasonState.Live), (stored.Settings.TeamPickDeadlineEastern, stored.State));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task ANewSeasonOpensAtBasicsAndTheResumeStepPersistsAuditsAndKeepsEverythingElse()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: true));
        Assert.Equal(SetupStep.Basics, created.Value!.SetupStep);

        var saved = await Run(service => service.UpdateSetupStepAsync(Admin, SetupStep.Money));
        var reloaded = await Run(service => service.GetSetupAsync());

        Assert.True(saved.IsSuccess);
        Assert.Equal(SetupStep.Money, reloaded.Value!.SetupStep);
        Assert.Equal((created.Value.Id, 2026, "2026 Season", true, SeasonState.Setup, created.Value.Settings), (reloaded.Value.Id, reloaded.Value.Year, reloaded.Value.Name, reloaded.Value.IsSimulation, reloaded.Value.State, reloaded.Value.Settings));
        Assert.Equal(created.Value.Pools, reloaded.Value.Pools);
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.step.updated"));
        Assert.Equal((Admin, created.Value.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("Basics", audit.Details);
        Assert.Contains("Money", audit.Details);
    }

    [Fact]
    public async Task RepeatingAResumeStepSucceedsWithoutASecondAuditEntry()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var first = await Run(service => service.UpdateSetupStepAsync(Admin, SetupStep.WeeklyDeadline));
        var replay = await Run(service => service.UpdateSetupStepAsync(Admin, SetupStep.WeeklyDeadline));

        Assert.True(first.IsSuccess && replay.IsSuccess);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.step.updated")));
    }

    [Fact]
    public async Task AnUnknownResumeStepIsRejectedWithoutChangingOrAuditing()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var result = await Run(service => service.UpdateSetupStepAsync(Admin, (SetupStep)42));

        Assert.Equal("season.step", result.Error!.Code);
        Assert.Equal(SetupStep.Basics, (await Run(service => service.GetSetupAsync())).Value!.SetupStep);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task ResumeStepWithNoSetupSeasonIsMissingAndALiveSeasonIsNeverChanged()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.ActivateWithDogs().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        var result = await Run(service => service.UpdateSetupStepAsync(Admin, SetupStep.Money));

        Assert.Equal("season.missing", result.Error!.Code);
        Assert.Equal(SetupStep.Basics, (await Run((service, database) => database.Seasons.SingleAsync())).SetupStep);
        Assert.Equal(0, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task ResumeStepWaitsForAnInFlightActivationAndThenLeavesTheLiveSeasonUntouched()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        await using var activating = CreateDatabase();
        await using var transaction = await activating.Database.BeginTransactionAsync();
        await activating.Database.ExecuteSqlRawAsync("""update league.seasons set "State" = 1""");

        var edit = Run(service => service.UpdateSetupStepAsync(Admin, SetupStep.Money));
        await Task.Delay(500);
        Assert.False(edit.IsCompleted);
        await transaction.CommitAsync();
        var result = await edit;

        Assert.False(result.IsSuccess);
        var stored = await Run((service, database) => database.Seasons.SingleAsync());
        Assert.Equal((SetupStep.Basics, SeasonState.Live), (stored.SetupStep, stored.State));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task ASetupSeasonSavedBeforeTheResumeColumnExistedOpensAtBasics()
    {
        await Migrate();
        var id = Guid.NewGuid();
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({id}, '100|USD|3|', 0)""");
        }

        var found = await Run(service => service.GetSetupAsync());

        Assert.Equal(SetupStep.Basics, found.Value!.SetupStep);
        Assert.True((await Run(service => service.UpdateSetupStepAsync(Admin, SetupStep.Money))).IsSuccess);
    }

    private static readonly string[] Divisions = ["AFC East", "AFC North", "AFC South", "AFC West", "NFC East", "NFC North", "NFC South", "NFC West"];

    private async Task<List<Team>> SeedTeams()
    {
        var teams = Divisions.SelectMany(division => Enumerable.Range(1, 4).Select(slot => new Team(new TeamId(Guid.NewGuid()), $"{division} Team {slot}", null, DateTimeOffset.UtcNow, division))).ToList();
        await using var database = CreateDatabase();
        database.Teams.AddRange(teams);
        await database.SaveChangesAsync();
        return teams;
    }

    private static List<TeamFinish> Finishes(List<Team> teams) => teams.GroupBy(team => team.Division).SelectMany(group => group.Select((team, index) => new TeamFinish(team.Id, index + 1))).ToList();

    [Fact]
    public async Task SetupTeamsListEveryCanonicalTeamWithoutFinishesUntilTheyAreSaved()
    {
        await Migrate();
        await SeedTeams();
        Assert.Equal("season.missing", (await Run(service => service.GetSetupTeamsAsync())).Error!.Code);
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var listed = await Run(service => service.GetSetupTeamsAsync());

        Assert.Equal(32, listed.Value!.Teams.Count);
        Assert.All(listed.Value.Teams, team => Assert.Null(team.FinishPosition));
        Assert.All(listed.Value.Teams, team => Assert.Null(team.PointValue));
        Assert.Equal(Divisions.Order(), listed.Value.Teams.Select(team => team.Division).Distinct().Order());
    }

    [Fact]
    public async Task SavingSetupTeamsPersistsOnlyTheSetupSeasonDerivesPointValuesAndAudits()
    {
        await Migrate();
        var teams = await SeedTeams();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.ActivateWithDogs().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            database.SeasonTeams.Add(new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), live.Id, teams[0].Id, teams[0].Division, 1, 0));
            await database.SaveChangesAsync();
        }

        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var saved = await Run(service => service.UpdateSetupTeamsAsync(Admin, Finishes(teams)));

        Assert.True(saved.IsSuccess);
        Assert.All(saved.Value!.Teams, team => Assert.Equal(team.FinishPosition, team.PointValue));
        var reloaded = await Run(service => service.GetSetupTeamsAsync());
        Assert.Equal(Finishes(teams).Select(entry => (entry.TeamId.Value, (int?)entry.Finish)).OrderBy(entry => entry.Value), reloaded.Value!.Teams.Select(team => (team.TeamId, team.FinishPosition)).OrderBy(entry => entry.TeamId));
        Assert.Equal(32, await Run((service, database) => database.SeasonTeams.CountAsync(team => team.SeasonId == created.Value!.Id)));
        Assert.Equal(1, await Run((service, database) => database.SeasonTeams.CountAsync(team => team.SeasonId == live.Id)));
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.teams.updated"));
        Assert.Equal((Admin, created.Value!.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
    }

    [Fact]
    public async Task RepeatingTheSameTeamsAddsNothingAndARealChangeReplacesThemWithOneMoreAuditEntry()
    {
        await Migrate();
        var teams = await SeedTeams();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var finishes = Finishes(teams);
        Assert.True((await Run(service => service.UpdateSetupTeamsAsync(Admin, finishes))).IsSuccess);

        Assert.True((await Run(service => service.UpdateSetupTeamsAsync(Admin, finishes))).IsSuccess);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.teams.updated")));

        // Swap 1st and 2nd in AFC East: the unique division finish index must not trip on the replacement.
        var east = teams.Where(team => team.Division == "AFC East").Take(2).ToList();
        var swapped = finishes.Select(entry => entry.TeamId == east[0].Id ? entry with { Finish = 2 } : entry.TeamId == east[1].Id ? entry with { Finish = 1 } : entry).ToList();
        Assert.True((await Run(service => service.UpdateSetupTeamsAsync(Admin, swapped))).IsSuccess);

        var reloaded = await Run(service => service.GetSetupTeamsAsync());
        Assert.Equal(2, reloaded.Value!.Teams.Single(team => team.TeamId == east[0].Id.Value).FinishPosition);
        Assert.Equal(32, await Run((service, database) => database.SeasonTeams.CountAsync()));
        Assert.Equal(2, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.teams.updated")));
    }

    [Fact]
    public async Task InvalidSetupTeamsAreRejectedWithoutSavingOrAuditing()
    {
        await Migrate();
        var teams = await SeedTeams();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var duplicateFinish = Finishes(teams);
        duplicateFinish[1] = duplicateFinish[1] with { Finish = 1 };

        var missingTeam = await Run(service => service.UpdateSetupTeamsAsync(Admin, Finishes(teams).Skip(1).ToList()));
        var badDivision = await Run(service => service.UpdateSetupTeamsAsync(Admin, duplicateFinish));

        Assert.Equal("season.teams", missingTeam.Error!.Code);
        Assert.Equal("season.teams", badDivision.Error!.Code);
        Assert.Contains("AFC East", badDivision.Error.Message);
        Assert.Equal(0, await Run((service, database) => database.SeasonTeams.CountAsync()));
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task SavingTeamsWithNoSetupSeasonIsMissingAndWritesNothing()
    {
        await Migrate();
        var teams = await SeedTeams();

        var result = await Run(service => service.UpdateSetupTeamsAsync(Admin, Finishes(teams)));

        Assert.Equal("season.missing", result.Error!.Code);
        Assert.Equal(0, await Run((service, database) => database.SeasonTeams.CountAsync()));
    }

    [Fact]
    public async Task TheDatabaseRefusesADuplicateTeamOrDivisionFinishInOneSeason()
    {
        await Migrate();
        var teams = await SeedTeams();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var east = teams.Where(team => team.Division == "AFC East").ToList();
        await using var database = CreateDatabase();
        database.SeasonTeams.Add(new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), created.Value!.Id, east[0].Id, "AFC East", 1, 0));
        await database.SaveChangesAsync();

        database.SeasonTeams.Add(new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), created.Value.Id, east[1].Id, "AFC East", 1, 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        database.SeasonTeams.Add(new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), created.Value.Id, east[0].Id, "AFC East", 2, 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task UpgradingWithDuplicateSeasonTeamsFailsLoudlyAndDeletesNothing()
    {
        await using (var database = CreateDatabase())
        {
            await database.GetService<IMigrator>().MigrateAsync("20261013203733_AddSetupStep");
            var (season, team) = (Guid.NewGuid(), Guid.NewGuid());
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({season}, '100|USD|3|', 1)""");
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into schedule.teams ("Id", "Name", "CreatedAt", "Division") values ({team}, 'A', now(), 'AFC East')""");
            foreach (var finish in new[] { 1, 1 })
                await database.Database.ExecuteSqlInterpolatedAsync($"""insert into schedule.season_teams ("Id", "SeasonId", "TeamId", "Division", "FinishPosition", "FinishTies") values ({Guid.NewGuid()}, {season}, {team}, 'AFC East', {finish}, 0)""");
        }

        await using var upgrading = CreateDatabase();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => upgrading.Database.MigrateAsync());

        Assert.Contains("unique season teams", failure.ToString());
        Assert.Equal(2, await upgrading.SeasonTeams.CountAsync());
    }

    // A Setup season with teams and a full imported schedule whose Week 12 holds Thanksgiving (Thu Nov 26) and Week 17 holds Christmas (Fri Dec 25).
    private async Task<List<Team>> SetupWithImportedSchedule(params ProviderGame[] extra)
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, [PG("thanksgiving", 12, teams[0], teams[1], new DateTimeOffset(2026, 11, 26, 17, 0, 0, TimeSpan.Zero)), PG("christmas", 17, teams[2], teams[3], new DateTimeOffset(2026, 12, 25, 18, 0, 0, TimeSpan.Zero)), .. extra]));
        var preview = await Run(service => service.PreviewScheduleAsync());
        Assert.True((await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!))).IsSuccess, preview.Error?.Message);
        return teams;
    }

    private Task<int> DogAudits() => Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.double-dogs.updated"));

    [Fact]
    public async Task DoubleDogWeeksNeedAnImportedScheduleAndASetupSeason()
    {
        await Migrate();
        Assert.Equal("season.missing", (await Run(service => service.GetSetupDoubleDogWeeksAsync())).Error!.Code);
        Assert.Equal("season.missing", (await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [2, 12, 17]))).Error!.Code);
        await SetupWithTeams();

        Assert.Equal("schedule.missing", (await Run(service => service.GetSetupDoubleDogWeeksAsync())).Error!.Code);
        var saved = await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [2, 12, 17]));

        Assert.Equal("schedule.missing", saved.Error!.Code);
        Assert.Empty((await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync())).DoubleDogWeekNumbers);
        Assert.Equal(0, await DogAudits());
    }

    [Fact]
    public async Task TheWeeksPageListsAllEighteenWeeksWithWeekTwoRequiredAndTheHolidayWeeksSuggestedFromTheSchedule()
    {
        await SetupWithImportedSchedule();
        var callsBefore = provider.Calls;

        var plan = await Run(service => service.GetSetupDoubleDogWeeksAsync());

        Assert.Equal(callsBefore, provider.Calls);
        Assert.Equal(Enumerable.Range(1, 18), plan.Value!.Weeks.Select(week => week.Week));
        Assert.Empty(plan.Value.Selected);
        var two = plan.Value.Weeks[1];
        Assert.True(two.Required);
        Assert.Contains("Required by the rules", two.Reason);
        var thanksgiving = plan.Value.Weeks[11];
        Assert.True(thanksgiving.Suggested);
        Assert.False(thanksgiving.Required);
        Assert.Contains("Suggested", thanksgiving.Reason);
        Assert.Contains("Thanksgiving (Thu Nov 26)", thanksgiving.Reason);
        var christmas = plan.Value.Weeks[16];
        Assert.True(christmas.Suggested);
        Assert.Contains("Christmas (Fri Dec 25)", christmas.Reason);
        Assert.Equal([2, 12, 17], plan.Value.Weeks.Where(week => week.Required || week.Suggested).Select(week => week.Week));
        Assert.All(plan.Value.Weeks.Where(week => week.Week is not (2 or 12 or 17)), week => Assert.Equal(("", false, false), (week.Reason, week.Suggested, week.Required)));
    }

    [Fact]
    public async Task HolidaySuggestionsFollowTheImportedScheduleNotTheSetupYearEditedAfterwards()
    {
        await SetupWithImportedSchedule();
        var before = await Run(service => service.GetSetupDoubleDogWeeksAsync());
        Assert.True((await Run(service => service.UpdateSetupBasicsAsync(Admin, 2027, "2027 Season", isSimulation: false))).IsSuccess);

        var after = await Run(service => service.GetSetupDoubleDogWeeksAsync());

        Assert.Equal([2, 12, 17], before.Value!.Weeks.Where(week => week.Required || week.Suggested).Select(week => week.Week));
        Assert.Equal([2, 12, 17], after.Value!.Weeks.Where(week => week.Required || week.Suggested).Select(week => week.Week));
        Assert.Contains("Thanksgiving (Thu Nov 26)", after.Value.Weeks[11].Reason);
        Assert.Contains("Christmas (Fri Dec 25)", after.Value.Weeks[16].Reason);
    }

    [Fact]
    public async Task AHolidayThatLandsInWeekTwoIsShownOnceAndKeepsBothReasons()
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("a", 2, teams[0], teams[1], new DateTimeOffset(2026, 11, 26, 17, 0, 0, TimeSpan.Zero))));
        var preview = await Run(service => service.PreviewScheduleAsync());
        Assert.True((await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!))).IsSuccess);

        var plan = await Run(service => service.GetSetupDoubleDogWeeksAsync());

        var week = plan.Value!.Weeks[1];
        Assert.True(week.Required && week.Suggested);
        Assert.Contains("Required by the rules", week.Reason);
        Assert.Contains("Thanksgiving", week.Reason);
        Assert.Equal(1, plan.Value.Weeks.Count(value => value.Reason.Contains("Thanksgiving")));
    }

    [Fact]
    public async Task SavingTheSelectionPersistsItSyncsTheCountAuditsOnceAndReloads()
    {
        await SetupWithImportedSchedule();

        var saved = await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [17, 2, 12]));

        Assert.True(saved.IsSuccess);
        Assert.Equal([2, 12, 17], saved.Value!.Selected);
        Assert.Equal([2, 12, 17], saved.Value.Weeks.Where(week => week.Selected).Select(week => week.Week));
        var season = await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync());
        Assert.Equal([2, 12, 17], season.DoubleDogWeekNumbers);
        Assert.Equal(3, season.Settings.DoubleDogWeeks);
        Assert.Equal([2, 12, 17], (await Run(service => service.GetSetupDoubleDogWeeksAsync())).Value!.Selected);
        Assert.Equal(1, await DogAudits());
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.double-dogs.updated"));
        Assert.Equal((Admin, season.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("17", audit.Details);
    }

    [Fact]
    public async Task ReplayingTheSameSelectionWritesNoAuditAndARealChangeWritesOneMore()
    {
        await SetupWithImportedSchedule();
        Assert.True((await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [2, 12, 17]))).IsSuccess);

        Assert.True((await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [12, 17, 2]))).IsSuccess);
        Assert.Equal(1, await DogAudits());
        var changed = await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [2, 12, 16, 17]));

        Assert.Equal([2, 12, 16, 17], changed.Value!.Selected);
        Assert.Equal(4, (await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync())).Settings.DoubleDogWeeks);
        Assert.Equal(2, await DogAudits());
    }

    [Theory]
    [InlineData(new int[] { })]
    [InlineData(new[] { 2, 12 })]
    [InlineData(new[] { 3, 12, 17 })]
    [InlineData(new[] { 2, 12, 12 })]
    [InlineData(new[] { 2, 12, 19 })]
    [InlineData(new[] { 0, 2, 12 })]
    [InlineData(new[] { 2, 3, 4, 5, 6 })]
    public async Task AnInvalidSelectionIsRejectedAndKeepsTheSavedOneWithoutAuditing(int[] weeks)
    {
        await SetupWithImportedSchedule();
        Assert.True((await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [2, 12, 17]))).IsSuccess);

        var result = await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, weeks));

        Assert.Equal("season.double-dogs", result.Error!.Code);
        var season = await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync());
        Assert.Equal([2, 12, 17], season.DoubleDogWeekNumbers);
        Assert.Equal(3, season.Settings.DoubleDogWeeks);
        Assert.Equal(1, await DogAudits());
    }

    [Fact]
    public async Task ASetupSeasonThatOnlyHasACountKeepsNoInventedSelectionAndCannotActivate()
    {
        await Migrate();
        var id = Guid.NewGuid();
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({id}, '100|USD|3|', 0)""");
        }

        var season = await Run((service, database) => database.Seasons.SingleAsync());

        Assert.Empty(season.DoubleDogWeekNumbers);
        Assert.Equal(3, season.Settings.DoubleDogWeeks);
        Assert.False(season.Activate().IsSuccess);
    }

    [Fact]
    public async Task ASavedSelectionMakesTheSeasonActivatable()
    {
        await SetupWithImportedSchedule();
        Assert.True((await Run(service => service.UpdateSetupDoubleDogWeeksAsync(Admin, [2, 12, 17]))).IsSuccess);

        var season = await Run((service, database) => database.Seasons.SingleAsync());

        Assert.True(season.Activate().IsSuccess);
    }

    private static PayoutPlace PP(int position, decimal percent) => new(position, percent);

    private static List<PayoutPlan> Plans(params PayoutPlace[] main) =>
        [new("Main", main), new("Dog", [PP(1, 100m)]), new("Regular Season", [PP(1, 100m)]), new("Playoff", [PP(1, 100m)])];

    private Task<int> PayoutAudits() => Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.fines-and-payouts.updated"));

    private async Task AddMembership(SeasonId season, MembershipRole role, bool active = true)
    {
        var membership = new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.NewGuid()), season, role);
        if (!active) membership.Deactivate();
        await using var database = CreateDatabase();
        database.Memberships.Add(membership);
        await database.SaveChangesAsync();
    }

    [Fact]
    public async Task ANewSeasonReportsAFiveDollarFineAndUnconfiguredPlansWithZeroCoaches()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var plan = await Run(service => service.GetSetupFinesAndPayoutsAsync());

        Assert.True(plan.IsSuccess);
        Assert.Equal((false, 5.00m, 0), (plan.Value!.Configured, plan.Value.MissedPickFine, plan.Value.ActiveCoaches));
        Assert.Equal(["Main", "Dog", "Regular Season", "Playoff"], plan.Value.Pools.Select(pool => pool.Pool));
        Assert.Equal([60m, 20m, 10m, 10m], plan.Value.Pools.Select(pool => pool.Stake));
        Assert.All(plan.Value.Pools, pool => Assert.Equal((0m, 0), (pool.Pot, pool.Places.Count)));
    }

    [Fact]
    public async Task OnlyActiveCoachMembershipsOfThisSeasonAreCounted()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var season = created.Value!.Id;
        await AddMembership(season, MembershipRole.Coach);
        await AddMembership(season, MembershipRole.Coach);
        await AddMembership(season, MembershipRole.Coach, active: false);
        await AddMembership(season, MembershipRole.Administrator);
        await AddMembership(season, MembershipRole.Commissioner);
        await AddMembership(new SeasonId(Guid.NewGuid()), MembershipRole.Coach);

        var plan = await Run(service => service.GetSetupFinesAndPayoutsAsync());

        Assert.Equal(2, plan.Value!.ActiveCoaches);
        Assert.Equal([120m, 40m, 20m, 20m], plan.Value.Pools.Select(pool => pool.Pot));
    }

    [Fact]
    public async Task SavingPersistsTheFineAndPlansAndTheCalculatorSplitsEachPotExactly()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        for (var coach = 0; coach < 7; coach++) await AddMembership(created.Value!.Id, MembershipRole.Coach);

        var saved = await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(7.50m, "USD"), Plans(PP(1, 33.33m), PP(2, 33.33m), PP(3, 33.34m))));
        var reloaded = await Run(service => service.GetSetupFinesAndPayoutsAsync());

        Assert.True(saved.IsSuccess);
        Assert.Equal((true, 7.50m, 7), (reloaded.Value!.Configured, reloaded.Value.MissedPickFine, reloaded.Value.ActiveCoaches));
        var main = reloaded.Value.Pools[0];
        Assert.Equal(420m, main.Pot);
        Assert.Equal([(1, 33.33m), (2, 33.33m), (3, 33.34m)], main.Places.Select(place => (place.Position, place.Percent)));
        Assert.Equal(main.Pot, main.Places.Sum(place => place.Amount));
        Assert.Equal([140.00m, 139.98m, 140.02m], main.Places.Select(place => place.Amount));
        Assert.All(reloaded.Value.Pools, pool => Assert.Equal(pool.Pot, pool.Places.Sum(place => place.Amount)));
        Assert.Equal(1, await PayoutAudits());
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.fines-and-payouts.updated"));
        Assert.Equal((Admin, created.Value!.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("7.5", audit.Details);
        Assert.Contains("33.33", audit.Details);
    }

    [Fact]
    public async Task ReplayingTheSameConfigurationWritesNoAuditAndARealChangeWritesOneMore()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        var plans = Plans(PP(1, 70m), PP(2, 30m));
        Assert.True((await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(5m, "USD"), plans))).IsSuccess);

        Assert.True((await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(5.00m, "USD"), Plans(PP(2, 30m), PP(1, 70m))))).IsSuccess);
        Assert.Equal(1, await PayoutAudits());
        Assert.True((await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(6m, "USD"), plans))).IsSuccess);

        Assert.Equal(2, await PayoutAudits());
    }

    [Theory]
    [InlineData("5", "70", "30")]
    [InlineData("5.0", "70.0", "30.0")]
    [InlineData("5.00", "70.00", "30.00")]
    public async Task ResavingTheSameValuesWithADifferentDecimalScaleWritesNoAudit(string fine, string first, string second)
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        Assert.True((await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(5.00m, "USD"), Plans(PP(1, 70.00m), PP(2, 30.00m))))).IsSuccess);
        Assert.Equal(1, await PayoutAudits());

        var replay = await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(decimal.Parse(fine), "USD"), Plans(PP(1, decimal.Parse(first)), PP(2, decimal.Parse(second)))));

        Assert.True(replay.IsSuccess);
        Assert.Equal(1, await PayoutAudits());
    }

    [Fact]
    public async Task AnInvalidConfigurationIsRejectedAndKeepsTheSavedOneWithoutAuditing()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        Assert.True((await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(5m, "USD"), Plans(PP(1, 100m))))).IsSuccess);

        var badTotal = await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(9m, "USD"), Plans(PP(1, 60m), PP(2, 30m))));
        var badFine = await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(0m, "USD"), Plans(PP(1, 100m))));
        var badPools = await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(5m, "USD"), [new("Main", [PP(1, 100m)])]));

        Assert.All(new[] { badTotal, badFine, badPools }, result => Assert.Equal("season.payouts", result.Error!.Code));
        var kept = await Run(service => service.GetSetupFinesAndPayoutsAsync());
        Assert.Equal(5.00m, kept.Value!.MissedPickFine);
        Assert.Equal([(1, 100m)], kept.Value.Pools[0].Places.Select(place => (place.Position, place.Percent)));
        Assert.Equal(1, await PayoutAudits());
    }

    [Fact]
    public async Task ASeasonSavedBeforeFinesExistedIsUnconfiguredAndInventsNothing()
    {
        await Migrate();
        var id = Guid.NewGuid();
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({id}, '100|USD|3|', 0)""");
        }

        var plan = await Run(service => service.GetSetupFinesAndPayoutsAsync());
        var season = await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync());

        Assert.Equal((false, (decimal?)null), (plan.Value!.Configured, plan.Value.MissedPickFine));
        Assert.Null(season.MissedPickFine);
        Assert.Empty(season.PayoutPlans);
    }

    [Fact]
    public async Task FinesAndPayoutsWithNoSetupSeasonAreMissingAndALiveSeasonIsNeverChanged()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.ActivateWithDogs().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        Assert.Equal("season.missing", (await Run(service => service.GetSetupFinesAndPayoutsAsync())).Error!.Code);
        Assert.Equal("season.missing", (await Run(service => service.UpdateSetupFinesAndPayoutsAsync(Admin, new Money(9m, "USD"), Plans(PP(1, 100m))))).Error!.Code);
        var stored = await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync());
        Assert.Equal((new Money(5.00m, "USD"), 0), (stored.MissedPickFine, stored.PayoutPlans.Count));
        Assert.Equal(0, await PayoutAudits());
    }

    private static LeagueRules CustomRules() => LeagueRules.Create(false, PointColors.Create([new PointColor(1, ColorTier.Red), new PointColor(2, ColorTier.Yellow), new PointColor(3, ColorTier.Blue), new PointColor(4, ColorTier.Green)]).Value!, RecordHolderMode.MostOccurrences).Value!;

    private Task<int> RuleAudits() => Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.league-settings.updated"));

    [Fact]
    public async Task ANewSeasonReadsTheLeagueDefaults()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));

        var plan = await Run(service => service.GetSetupLeagueSettingsAsync());

        Assert.True(plan.IsSuccess);
        Assert.True(plan.Value!.TeamMayAlsoBeDog);
        Assert.Equal(RecordHolderMode.FirstOccurrence, plan.Value.RecordHolder);
        Assert.Equal([(1, ColorTier.Green), (2, ColorTier.Blue), (3, ColorTier.Yellow), (4, ColorTier.Red)], plan.Value.PointColors.Select(entry => (entry.Points, entry.Color)));
    }

    [Fact]
    public async Task SavingPersistsAllThreeSettingsAuditsBeforeAndAfterAndKeepsEveryOtherSetting()
    {
        await Migrate();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: true));
        Assert.True((await Run(service => service.UpdateSetupDeadlineAsync(Admin, new TimeOnly(11, 30)))).IsSuccess);
        var before = (await Run(service => service.GetSetupAsync())).Value!;

        var saved = await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, CustomRules()));
        var reloaded = (await Run(service => service.GetSetupAsync())).Value!;

        Assert.True(saved.IsSuccess);
        var plan = (await Run(service => service.GetSetupLeagueSettingsAsync())).Value!;
        Assert.False(plan.TeamMayAlsoBeDog);
        Assert.Equal(RecordHolderMode.MostOccurrences, plan.RecordHolder);
        Assert.Equal([(1, ColorTier.Red), (2, ColorTier.Yellow), (3, ColorTier.Blue), (4, ColorTier.Green)], plan.PointColors.Select(entry => (entry.Points, entry.Color)));
        Assert.Equal(before.Settings with { Rules = CustomRules() }, reloaded.Settings);
        Assert.Equal((before.Id, before.Year, before.Name, before.IsSimulation), (reloaded.Id, reloaded.Year, reloaded.Name, reloaded.IsSimulation));
        Assert.Equal(new TimeOnly(11, 30), reloaded.Settings.TeamPickDeadlineEastern);
        Assert.Equal(before.Pools, reloaded.Pools);
        Assert.Equal(1, await RuleAudits());
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.league-settings.updated"));
        Assert.Equal((Admin, created.Value!.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("Green", audit.Details);
        Assert.Contains("Red", audit.Details);
        Assert.Contains("MostOccurrences", audit.Details);
        Assert.Contains("FirstOccurrence", audit.Details);
    }

    [Fact]
    public async Task ReplayingTheSameRulesWritesNoAuditAndARealChangeWritesOneMore()
    {
        await Migrate();
        await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        Assert.True((await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, LeagueRules.Default))).IsSuccess);
        Assert.Equal(0, await RuleAudits());
        Assert.True((await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, CustomRules()))).IsSuccess);

        Assert.True((await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, CustomRules()))).IsSuccess);
        Assert.Equal(1, await RuleAudits());
        Assert.True((await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, CustomRules() with { TeamMayAlsoBeDog = true }))).IsSuccess);

        Assert.Equal(2, await RuleAudits());
    }

    [Fact]
    public async Task SeasonsSavedBeforeTheRulesExistedLoadTheDefaultsAndKeepEveryOtherField()
    {
        await Migrate();
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({Guid.NewGuid()}, '125.50|USD|4|', 1)""");
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({Guid.NewGuid()}, '100|USD|3|11:30 AM', 0)""");
        }

        var seasons = await Run((service, database) => database.Seasons.AsNoTracking().OrderBy(season => season.State).ToListAsync());

        Assert.All(seasons, season => Assert.Equal(LeagueRules.Default, season.Settings.Rules));
        Assert.Equal((new Money(100m, "USD"), 3, new TimeOnly(11, 30)), (seasons[0].Settings.BuyIn, seasons[0].Settings.DoubleDogWeeks, seasons[0].Settings.TeamPickDeadlineEastern));
        Assert.Equal((new Money(125.50m, "USD"), 4, (TimeOnly?)null), (seasons[1].Settings.BuyIn, seasons[1].Settings.DoubleDogWeeks, seasons[1].Settings.TeamPickDeadlineEastern));
        var saved = await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, CustomRules()));
        Assert.True(saved.IsSuccess);
        var setup = (await Run(service => service.GetSetupAsync())).Value!;
        Assert.Equal((new Money(100m, "USD"), 3, new TimeOnly(11, 30)), (setup.Settings.BuyIn, setup.Settings.DoubleDogWeeks, setup.Settings.TeamPickDeadlineEastern));
    }

    [Fact]
    public async Task ALegacyStoredValueWithGarbledRulesFallsBackToTheDefaultsInsteadOfFailingToLoad()
    {
        await Migrate();
        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""insert into league.seasons ("Id", "Settings", "State") values ({Guid.NewGuid()}, '100|USD|3||maybe|Green,Green,Blue,Red|Sometimes', 1)""");
        }

        var season = await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync());

        Assert.Equal(LeagueRules.Default, season.Settings.Rules);
        Assert.Equal(new Money(100m, "USD"), season.Settings.BuyIn);
    }

    [Fact]
    public async Task LeagueSettingsWithNoSetupSeasonAreMissingAndALiveSeasonIsNeverChanged()
    {
        await Migrate();
        var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Season", isSimulation: false).Value!;
        Assert.True(live.ActivateWithDogs().IsSuccess);
        await using (var database = CreateDatabase())
        {
            database.Seasons.Add(live);
            await database.SaveChangesAsync();
        }

        Assert.Equal("season.missing", (await Run(service => service.GetSetupLeagueSettingsAsync())).Error!.Code);
        Assert.Equal("season.missing", (await Run(service => service.UpdateSetupLeagueSettingsAsync(Admin, CustomRules()))).Error!.Code);
        Assert.Equal(LeagueRules.Default, (await Run((service, database) => database.Seasons.AsNoTracking().SingleAsync())).Settings.Rules);
        Assert.Equal(0, await RuleAudits());
    }

    private async Task Migrate()
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
    }

    private readonly FakeScheduleProvider provider = new();
    private readonly MemoryCache previews = new(new MemoryCacheOptions());

    private sealed class FakeScheduleProvider : IScheduleProvider
    {
        public Result<ProviderSchedule> Reply { get; set; } = Result<ProviderSchedule>.Success(new ProviderSchedule(FetchedAt, []));
        public int Calls { get; private set; }

        public Task<Result<ProviderSchedule>> FetchRegularSeasonAsync(int season, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Reply);
        }
    }

    private static readonly DateTimeOffset FetchedAt = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Kickoff = new(2026, 9, 13, 17, 0, 0, TimeSpan.Zero);

    private static ProviderGame PG(string id, int week, Team home, Team away, DateTimeOffset? kickoff = null) => new(id, week, home.Name, away.Name, kickoff ?? Kickoff.AddDays((week - 1) * 7));

    // The given games plus a filler game (teams 6 v 7) in every regular-season week they leave out: a schedule must cover weeks 1 to 18.
    private static ProviderGame[] Full(List<Team> teams, params ProviderGame[] games) => games
        .Concat(Enumerable.Range(1, 18).Where(week => games.All(game => game.Week != week)).Select(week => PG($"filler-{week}", week, teams[6], teams[7])))
        .ToArray();

    private void ProviderReturns(params ProviderGame[] games) => provider.Reply = Result<ProviderSchedule>.Success(new ProviderSchedule(FetchedAt, games));

    // A Setup season with all 32 teams entered, ready for a schedule.
    private async Task<(List<Team> Teams, SeasonSummary Season)> SetupWithTeams()
    {
        await Migrate();
        var teams = await SeedTeams();
        var created = await Run(service => service.CreateBlankAsync(Admin, 2026, "2026 Season", isSimulation: false));
        Assert.True((await Run(service => service.UpdateSetupTeamsAsync(Admin, Finishes(teams)))).IsSuccess);
        return (teams, created.Value!);
    }

    private Task<(int Weeks, int Games, int Audits)> Counts() => Run(async (service, database) =>
    {
        var weeks = await database.Weeks.AsNoTracking().ToListAsync();
        return (weeks.Count, weeks.Sum(week => week.Games.Count), await database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.schedule.imported"));
    });

    [Fact]
    public async Task LoadingTheWizardNeverCallsTheProvider()
    {
        var (_, _) = await SetupWithTeams();

        await Run(service => service.GetSetupAsync());
        await Run(service => service.GetSetupTeamsAsync());
        var schedule = await Run(service => service.GetSetupScheduleAsync());

        Assert.Equal(0, provider.Calls);
        Assert.False(schedule.Value!.Imported);
        Assert.Empty(schedule.Value.Weeks);
    }

    [Fact]
    public async Task PreviewGroupsTheProviderGamesByWeekWithoutSavingAnything()
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("11", 2, teams[2], teams[3]), PG("10", 1, teams[0], teams[1]), PG("12", 1, teams[4], teams[5])));

        var preview = await Run(service => service.PreviewScheduleAsync());

        Assert.True(preview.IsSuccess);
        Assert.Equal(1, provider.Calls);
        Assert.False(preview.Value!.Imported);
        Assert.Equal(Enumerable.Range(1, 18), preview.Value.Weeks.Select(week => week.Week));
        var game = preview.Value.Weeks[0].Games.Single(value => value.ProviderGameId == "10");
        Assert.Equal((teams[0].Id.Value, teams[0].Name, teams[1].Id.Value, teams[1].Name, Kickoff), (game.HomeTeamId, game.HomeTeam, game.AwayTeamId, game.AwayTeam, game.Kickoff));
        Assert.Equal(19, preview.Value.GameCount);
        Assert.False(string.IsNullOrEmpty(preview.Value.Fingerprint));
        Assert.Equal((0, 0, 0), await Counts());
    }

    [Fact]
    public async Task AnUnpublishedSeasonPreviewsAsEmptyAndProviderProblemsAreReportedWithoutWriting()
    {
        await SetupWithTeams();
        var empty = await Run(service => service.PreviewScheduleAsync());
        provider.Reply = Result<ProviderSchedule>.Failure("schedule.config", "The schedule provider is not configured.");
        var unconfigured = await Run(service => service.PreviewScheduleAsync());
        provider.Reply = Result<ProviderSchedule>.Failure("schedule.provider", "The schedule provider answered HTTP 500.");
        var down = await Run(service => service.PreviewScheduleAsync());

        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value!.Weeks);
        Assert.Equal("schedule.config", unconfigured.Error!.Code);
        Assert.Equal("schedule.provider", down.Error!.Code);
        Assert.Equal((0, 0, 0), await Counts());
    }

    [Fact]
    public async Task ImportingSavesEveryWeekAndGameAtomicallyWithProviderIdsAndProvenance()
    {
        var (teams, season) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1]), PG("11", 1, teams[2], teams[3]), PG("20", 18, teams[4], teams[5])));
        var preview = await Run(service => service.PreviewScheduleAsync());

        var imported = await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!));

        Assert.True(imported.IsSuccess);
        Assert.True(imported.Value!.Imported);
        Assert.Equal(1, provider.Calls);
        Assert.Equal((18, 19, 1), await Counts());
        var weeks = await Run((service, database) => database.Weeks.AsNoTracking().OrderBy(week => week.Number).ToListAsync());
        Assert.Equal(Enumerable.Range(1, 18), weeks.Select(week => week.Number));
        Assert.All(weeks, week => Assert.Equal((season.Id, WeekKind.Regular, WeekState.Draft), (week.SeasonId, week.Type.Kind, week.State)));
        var first = weeks[0].Games.Single(game => game.ProviderGameId == "10");
        Assert.Equal((teams[0].Id, teams[1].Id, Kickoff), (first.HomeTeamId, first.AwayTeamId, first.Kickoff.Value));
        var audit = await Run((service, database) => database.AuditEntries.SingleAsync(entry => entry.Action == "season.setup.schedule.imported"));
        Assert.Equal((Admin, season.Id.Value.ToString()), (audit.UserId, audit.ResourceId));
        Assert.Contains("BALLDONTLIE", audit.Details);
        Assert.Contains(preview.Value.Fingerprint!, audit.Details);
        Assert.Contains("2026-10-14T12:00:00", audit.Details);
        var reloaded = await Run(service => service.GetSetupScheduleAsync());
        Assert.True(reloaded.Value!.Imported);
        Assert.Equal(19, reloaded.Value.GameCount);
    }

    [Fact]
    public async Task ASecondImportOrPreviewNeverOverwritesAnImportedSchedule()
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1])));
        var preview = await Run(service => service.PreviewScheduleAsync());
        Assert.True((await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!))).IsSuccess);

        var again = await Run(service => service.ImportScheduleAsync(Admin, preview.Value.Fingerprint!));
        var previewAgain = await Run(service => service.PreviewScheduleAsync());

        Assert.Equal("schedule.exists", again.Error!.Code);
        Assert.Equal("schedule.exists", previewAgain.Error!.Code);
        Assert.Equal((18, 18, 1), await Counts());
    }

    [Fact]
    public async Task ImportSavesExactlyWhatWasPreviewedEvenIfTheProviderChangedAfterwardsWithoutAskingItAgain()
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1])));
        var preview = await Run(service => service.PreviewScheduleAsync());
        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1], Kickoff.AddHours(3)), PG("99", 2, teams[2], teams[3])));

        var imported = await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!));

        Assert.True(imported.IsSuccess);
        Assert.Equal(1, provider.Calls);
        var game = (await Run((service, database) => database.Weeks.AsNoTracking().SingleAsync(week => week.Number == 1))).Games.Single(value => value.ProviderGameId == "10");
        Assert.Equal(("10", Kickoff), (game.ProviderGameId, game.Kickoff.Value));
    }

    [Fact]
    public async Task APreviewDoesNotSurviveAChangeOfTheSetupSeasonYear()
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1])));
        var preview = await Run(service => service.PreviewScheduleAsync());
        Assert.True((await Run(service => service.UpdateSetupBasicsAsync(Admin, 2027, "2027 Season", isSimulation: false))).IsSuccess);

        var imported = await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!));

        Assert.Equal("schedule.changed", imported.Error!.Code);
        Assert.Equal((0, 0, 0), await Counts());

        // Back on the year it was fetched for, previewing and importing work normally.
        Assert.True((await Run(service => service.UpdateSetupBasicsAsync(Admin, 2026, "2026 Season", isSimulation: false))).IsSuccess);
        var again = await Run(service => service.PreviewScheduleAsync());
        Assert.True((await Run(service => service.ImportScheduleAsync(Admin, again.Value!.Fingerprint!))).IsSuccess);
    }

    [Fact]
    public async Task ImportRefusesAFingerprintThatWasNeverPreviewedOrHasExpiredAndNeverCallsTheProvider()
    {
        var (teams, _) = await SetupWithTeams();
        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1])));
        var preview = await Run(service => service.PreviewScheduleAsync());
        var forged = await Run(service => service.ImportScheduleAsync(Admin, "ABC"));
        previews.Clear();

        var expired = await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!));

        Assert.Equal("schedule.changed", forged.Error!.Code);
        Assert.Equal("schedule.changed", expired.Error!.Code);
        Assert.Equal(1, provider.Calls);
        Assert.Equal((0, 0, 0), await Counts());
    }

    [Fact]
    public async Task EveryKindOfBadProviderDataIsRejectedAndLeavesTheDatabaseUnchanged()
    {
        var (teams, _) = await SetupWithTeams();
        var outsider = new Team(new TeamId(Guid.NewGuid()), "Outside Owls", null, DateTimeOffset.UtcNow, "AFC East");
        await using (var database = CreateDatabase())
        {
            database.Teams.Add(outsider);
            await database.SaveChangesAsync();
        }

        var cases = new (string Name, ProviderGame[] Games)[]
        {
            ("no games", []),
            ("week 19", [PG("1", 19, teams[0], teams[1])]),
            ("week 0", [PG("1", 0, teams[0], teams[1])]),
            ("duplicate id", [PG("1", 1, teams[0], teams[1]), PG("1", 2, teams[2], teams[3])]),
            ("unknown team", [new ProviderGame("1", 1, "Nowhere Navigators", teams[1].Name, Kickoff)]),
            ("team outside the season", [PG("1", 1, teams[0], outsider)]),
            ("malformed kickoff", [new ProviderGame("1", 1, teams[0].Name, teams[1].Name, null)]),
            ("missing week 18", Enumerable.Range(1, 17).Select(week => PG($"w{week}", week, teams[0], teams[1])).ToArray()),
        };
        foreach (var (name, games) in cases)
        {
            ProviderReturns(games);
            var preview = await Run(service => service.PreviewScheduleAsync());
            var imported = await Run(service => service.ImportScheduleAsync(Admin, "ANY"));

            if (name == "no games") Assert.True(preview.IsSuccess);
            else Assert.Equal("schedule.invalid", preview.Error!.Code);
            Assert.Equal("schedule.changed", imported.Error!.Code);
            Assert.Equal((0, 0, 0), await Counts());
        }
    }

    [Fact]
    public async Task AGameAlreadyImportedElsewhereIsRefusedByTheDatabaseAndNothingIsSaved()
    {
        var (teams, _) = await SetupWithTeams();
        var other = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        other.AddGame(new Game(new GameId(Guid.NewGuid()), teams[0].Id, teams[1].Id, new Kickoff(Kickoff), providerGameId: "10"));
        await using (var database = CreateDatabase())
        {
            database.Weeks.Add(other);
            await database.SaveChangesAsync();
        }

        ProviderReturns(Full(teams, PG("10", 1, teams[0], teams[1]), PG("11", 1, teams[2], teams[3])));
        var preview = await Run(service => service.PreviewScheduleAsync());
        var imported = await Run(service => service.ImportScheduleAsync(Admin, preview.Value!.Fingerprint!));

        Assert.Equal("schedule.exists", imported.Error!.Code);
        Assert.Equal((1, 1, 0), await Counts());
    }

    [Fact]
    public async Task TheDatabaseRefusesTwoGamesWithTheSameProviderIdButAllowsManyHandEnteredOnes()
    {
        await Migrate();
        var teams = await SeedTeams();
        await using var database = CreateDatabase();
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        for (var index = 0; index < 3; index++) week.AddGame(new Game(new GameId(Guid.NewGuid()), teams[0].Id, teams[1].Id, new Kickoff(Kickoff)));
        database.Weeks.Add(week);
        await database.SaveChangesAsync();

        var second = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        second.AddGame(new Game(new GameId(Guid.NewGuid()), teams[0].Id, teams[1].Id, new Kickoff(Kickoff), providerGameId: "5"));
        second.AddGame(new Game(new GameId(Guid.NewGuid()), teams[2].Id, teams[3].Id, new Kickoff(Kickoff), providerGameId: "5"));
        database.Weeks.Add(second);
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task ScheduleCallsWithNoSetupSeasonAreMissingAndNeverReachTheProvider()
    {
        await Migrate();
        await SeedTeams();

        Assert.Equal("season.missing", (await Run(service => service.GetSetupScheduleAsync())).Error!.Code);
        Assert.Equal("season.missing", (await Run(service => service.PreviewScheduleAsync())).Error!.Code);
        Assert.Equal("season.missing", (await Run(service => service.ImportScheduleAsync(Admin, "ANY"))).Error!.Code);
        Assert.Equal(0, provider.Calls);
    }

    private Task<T> Run<T>(Func<SeasonSetupService, Task<T>> action) => Run((service, _) => action(service));

    private async Task<T> Run<T>(Func<SeasonSetupService, SwanDbContext, Task<T>> action)
    {
        await using var database = CreateDatabase();
        return await action(new SeasonSetupService(database, new AuditService(database, new SystemClock()), provider, previews, new SystemClock()), database);
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure()).Options);
}
