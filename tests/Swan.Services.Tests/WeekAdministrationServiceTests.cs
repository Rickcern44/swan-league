using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
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

    [Fact]
    public async Task ResultsRequireLockedWeekAndIncompleteWeeksCannotFinalize()
    {
        var seeded = await SeedLockedWeek();
        var draft = await SeedWeek();

        var early = await Run(service => service.ConfirmResultAsync(Admin, draft.Week, draft.Game, 21, 14));
        var negative = await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[0], -1, 14));
        var missingGame = await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, new GameId(Guid.NewGuid()), 1, 0));
        var missingWeek = await Run(service => service.FinalizeAsync(Admin, new WeekId(Guid.NewGuid())));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[0], 21, 14));
        var incomplete = await Run(service => service.FinalizeAsync(Admin, seeded.Week));

        Assert.Equal("week.state", early.Error!.Code);
        Assert.Equal("game-result.invalid", negative.Error!.Code);
        Assert.Equal("game.missing", missingGame.Error!.Code);
        Assert.Equal("week.missing", missingWeek.Error!.Code);
        Assert.Equal("week.results", incomplete.Error!.Code);
        var week = (await Run(service => service.GetAsync(seeded.Week))).Value!;
        Assert.Equal(WeekState.Locked, week.State);
        Assert.Equal(1, week.Games.Count(game => game.HasResult));
        Assert.Equal(0, await Run((service, database) => database.WeekScores.CountAsync()));
        Assert.Equal(["week.result.confirmed"], await Run((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task FinalizeRebuildsWeekScoresAndRecomputeReplacesInsteadOfAppending()
    {
        var seeded = await SeedLockedWeek();
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[0], 21, 14));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[1], 17, 17));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[2], 3, 10));

        var finalized = await Run(service => service.FinalizeAsync(Admin, seeded.Week));
        var again = await Run(service => service.RecomputeAsync(Admin, seeded.Week));

        Assert.True(finalized.IsSuccess);
        Assert.True(again.IsSuccess);
        Assert.Equal(WeekState.Final, finalized.Value!.State);
        var scores = await LoadScores(seeded.Week);
        var score = Assert.Single(scores);
        Assert.Equal(seeded.Membership, score.MembershipId);
        Assert.Equal(3, score.Lines.Count);
        Assert.Equal(1, score.Lines.Sum(line => line.Points));

        // Correct the stored source (tie becomes a home win), then recompute: lines are replaced, not appended.
        await CorrectStoredResult(seeded.Games[1], 24, 17);
        var recomputed = await Run(service => service.RecomputeAsync(Admin, seeded.Week));

        Assert.True(recomputed.IsSuccess);
        var replaced = Assert.Single(await LoadScores(seeded.Week));
        Assert.Equal(score.Id, replaced.Id);
        Assert.Equal(3, replaced.Lines.Count);
        Assert.Equal(2, replaced.Lines.Sum(line => line.Points));
        Assert.Equal(["week.result.confirmed", "week.result.confirmed", "week.result.confirmed", "week.finalized", "week.recomputed", "week.recomputed"],
            await Run((service, database) => database.AuditEntries.OrderBy(entry => entry.At).Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task RecomputeRequiresFinalWeekAndPlayoffWeeksAreNotScoredHere()
    {
        var locked = await SeedLockedWeek();
        var playoff = await SeedLockedWeek(WeekKind.Playoff);
        await Run(service => service.ConfirmResultAsync(Admin, playoff.Week, playoff.Games[0], 1, 0));
        await Run(service => service.ConfirmResultAsync(Admin, playoff.Week, playoff.Games[1], 1, 0));
        await Run(service => service.ConfirmResultAsync(Admin, playoff.Week, playoff.Games[2], 1, 0));

        var notFinal = await Run(service => service.RecomputeAsync(Admin, locked.Week));
        var unsupported = await Run(service => service.FinalizeAsync(Admin, playoff.Week));

        Assert.Equal("week.state", notFinal.Error!.Code);
        Assert.Equal("week.scoring", unsupported.Error!.Code);
        Assert.Equal(WeekState.Locked, (await Run(service => service.GetAsync(playoff.Week))).Value!.State);
        Assert.Equal(0, await Run((service, database) => database.WeekScores.CountAsync()));
    }

    [Fact]
    public async Task DuplicateBallotsForOneMemberBlockScoringWithoutChangingTheWeek()
    {
        var seeded = await SeedLockedWeek();
        await using (var database = CreateDatabase())
        {
            database.Ballots.Add(new Ballot(new BallotId(Guid.NewGuid()), seeded.Membership, seeded.Week));
            await database.SaveChangesAsync();
        }

        foreach (var game in seeded.Games) await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, game, 1, 0));
        var finalized = await Run(service => service.FinalizeAsync(Admin, seeded.Week));

        Assert.Equal("week.ballots", finalized.Error!.Code);
        Assert.Equal(WeekState.Locked, (await Run(service => service.GetAsync(seeded.Week))).Value!.State);
        Assert.Equal(0, await Run((service, database) => database.WeekScores.CountAsync()));
        Assert.DoesNotContain("week.finalized", await Run((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    private sealed record LockedWeek(WeekId Week, List<GameId> Games, MembershipId Membership);

    private async Task<LockedWeek> SeedLockedWeek(WeekKind kind = WeekKind.Regular)
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(kind, kind == WeekKind.Regular ? null : "WC", true));
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), week.Id);
        var games = new List<GameId>();
        for (var index = 0; index < 3; index++)
        {
            var game = new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow.AddDays(3)));
            week.AddGame(game);
            games.Add(game.Id);
            Assert.True(week.AddLine(game.Id, new Spread(-3m)).IsSuccess);
            Assert.True(week.ApproveLatestLine(game.Id).IsSuccess);
            Assert.True(ballot.AddPick(new TeamPick(game.HomeTeamId, game.Id, DateTimeOffset.UtcNow.AddDays(2)), DateTimeOffset.UtcNow).IsSuccess);
        }

        Assert.True(week.ApproveLines().IsSuccess);
        Assert.True(week.Publish().IsSuccess);
        Assert.True(week.Lock().IsSuccess);
        database.Weeks.Add(week);
        database.Ballots.Add(ballot);
        await database.SaveChangesAsync();
        return new LockedWeek(week.Id, games, ballot.MembershipId);
    }

    private async Task<List<WeekScore>> LoadScores(WeekId weekId)
    {
        await using var database = CreateDatabase();
        return await database.WeekScores.AsNoTracking().Where(score => score.WeekId == weekId).ToListAsync();
    }

    // A Final week rejects result edits (reopen is a later slice), so correct the stored source directly.
    private async Task CorrectStoredResult(GameId gameId, int home, int away)
    {
        await using var database = CreateDatabase();
        var changed = await database.Database.ExecuteSqlInterpolatedAsync($"""update schedule.games set "Result" = {$"{home}:{away}"} where "Id" = {gameId.Value}""");
        Assert.Equal(1, changed);
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
