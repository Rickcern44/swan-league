using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Platform;
using Swan.Services.Schedule;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class WeekAdministrationServiceTests : IAsyncLifetime
{
    private static readonly UserId Admin = new(Guid.Parse("9c9b2c10-a312-4b77-a26a-ef546e87f3c9"));
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task DraftWeekMovesThroughLifecycleAndEveryActionIsAudited()
    {
        var (weekId, gameId) = await SeedWeek();

        var added = await Run(service => service.AddLineAsync(Admin, weekId, gameId, -3.5m, approve: true));
        var approved = await Run(service => service.ApproveLinesAsync(Admin, weekId));
        var published = await Run(service => service.PublishAsync(Admin, weekId));
        var locked = await Run(service => service.LockAsync(Admin, weekId));

        Assert.All(new[] { added, approved, published, locked }, result => Assert.True(result.IsSuccess));
        var week = (await Run(service => service.GetAsync(weekId))).Value!;
        Assert.Equal(WeekState.Locked, week.State);
        Assert.Equal(-3.5m, week.Games.Single().ApprovedLine);
        var actions = await Run(async (service, database) => (await database.AuditEntries.AsNoTracking().OrderBy(entry => entry.At).ToListAsync()).Select(entry => (entry.Action, entry.UserId, entry.ResourceId)).ToList());
        Assert.Equal(["week.line.added", "week.lines.approved", "week.published", "week.locked"], actions.Select(entry => entry.Action));
        Assert.All(actions, entry => Assert.Equal((Admin, weekId.Value.ToString()), (entry.UserId, entry.ResourceId)));
    }

    [Fact]
    public async Task RejectedTransitionsLeaveWeekAndAuditUntouched()
    {
        var (weekId, _) = await SeedWeek();

        var publishDraft = await Run(service => service.PublishAsync(Admin, weekId));
        var lockDraft = await Run(service => service.LockAsync(Admin, weekId));
        var approveWithoutLines = await Run(service => service.ApproveLinesAsync(Admin, weekId));

        Assert.Equal("week.state", publishDraft.Error!.Code);
        Assert.Equal("week.state", lockDraft.Error!.Code);
        Assert.Equal("week.lines", approveWithoutLines.Error!.Code);
        Assert.Equal(WeekState.Draft, (await Run(service => service.GetAsync(weekId))).Value!.State);
        Assert.Equal(0, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task LinesFreezeOnceApprovedAndMissingTargetsAreReported()
    {
        var (weekId, gameId) = await SeedWeek();
        await Run(service => service.AddLineAsync(Admin, weekId, gameId, 2.5m, approve: true));
        await Run(service => service.ApproveLinesAsync(Admin, weekId));

        var frozen = await Run(service => service.AddLineAsync(Admin, weekId, gameId, 3m, approve: false));
        var missingGame = await Run(service => service.AddLineAsync(Admin, weekId, new GameId(Guid.NewGuid()), 3m, approve: false));
        var missingWeek = await Run(service => service.PublishAsync(Admin, new WeekId(Guid.NewGuid())));

        Assert.Equal("game.frozen", frozen.Error!.Code);
        Assert.Equal("game.missing", missingGame.Error!.Code);
        Assert.Equal("week.missing", missingWeek.Error!.Code);
        Assert.Equal(2, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task LineWithoutApprovalIsNotApprovedSoWeekCannotAdvance()
    {
        var (weekId, gameId) = await SeedWeek();

        var added = await Run(service => service.AddLineAsync(Admin, weekId, gameId, 1m, approve: false));
        var approveAll = await Run(service => service.ApproveLinesAsync(Admin, weekId));

        Assert.True(added.IsSuccess);
        Assert.Null((await Run(service => service.GetAsync(weekId))).Value!.Games.Single().ApprovedLine);
        Assert.Equal("week.lines", approveAll.Error!.Code);
    }

    [Fact]
    public async Task LatestLineApprovedAfterReloadAndHistoryIsExposedSeparatelyFromApprovedLine()
    {
        var (weekId, gameId) = await SeedWeek();
        await Run(service => service.AddLineAsync(Admin, weekId, gameId, -3m, approve: false));
        await Run(service => service.AddLineAsync(Admin, weekId, gameId, -4.5m, approve: false));
        var before = (await Run(service => service.GetAsync(weekId))).Value!.Games.Single();

        var approved = await Run(service => service.ApproveLatestLineAsync(Admin, weekId, gameId));

        Assert.Equal([-3m, -4.5m], before.LineHistory);
        Assert.Null(before.ApprovedLine);
        Assert.True(approved.IsSuccess);
        var game = (await Run(service => service.GetAsync(weekId))).Value!.Games.Single();
        Assert.Equal(-4.5m, game.ApprovedLine);
        Assert.Equal([-3m, -4.5m], game.LineHistory);
        var actions = await Run((service, database) => database.AuditEntries.AsNoTracking().OrderBy(entry => entry.At).Select(entry => entry.Action).ToListAsync());
        Assert.Equal(["week.line.added", "week.line.added", "week.line.approved"], actions);
    }

    [Fact]
    public async Task ApproveLatestLineRejectsMissingHistoryFrozenLinesAndMissingTargetsWithoutAudit()
    {
        var (weekId, gameId) = await SeedWeek();

        var noHistory = await Run(service => service.ApproveLatestLineAsync(Admin, weekId, gameId));
        var missingGame = await Run(service => service.ApproveLatestLineAsync(Admin, weekId, new GameId(Guid.NewGuid())));
        var missingWeek = await Run(service => service.ApproveLatestLineAsync(Admin, new WeekId(Guid.NewGuid()), gameId));
        await Run(service => service.AddLineAsync(Admin, weekId, gameId, 1m, approve: true));
        await Run(service => service.ApproveLinesAsync(Admin, weekId));
        var frozen = await Run(service => service.ApproveLatestLineAsync(Admin, weekId, gameId));

        Assert.Equal("game.line", noHistory.Error!.Code);
        Assert.Equal("game.missing", missingGame.Error!.Code);
        Assert.Equal("week.missing", missingWeek.Error!.Code);
        Assert.Equal("game.line", frozen.Error!.Code);
        Assert.Equal(2, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    private async Task<(WeekId Week, GameId Game)> SeedWeek()
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        var game = new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow.AddDays(3)));
        week.AddGame(game);
        database.Weeks.Add(week);
        await database.SaveChangesAsync();
        return (week.Id, game.Id);
    }

    private Task<T> Run<T>(Func<WeekAdministrationService, Task<T>> action) => Run((service, _) => action(service));

    private async Task<T> Run<T>(Func<WeekAdministrationService, SwanDbContext, Task<T>> action)
    {
        await using var database = CreateDatabase();
        var clock = new SystemClock();
        return await action(new WeekAdministrationService(database, new AuditService(database, clock)), database);
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
}
