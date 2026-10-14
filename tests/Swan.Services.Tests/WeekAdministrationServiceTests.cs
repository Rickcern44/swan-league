using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Core.Scoring;
using Swan.Data;
using Swan.Services.Platform;
using Swan.Services.Scoring;
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

    [Fact]
    public async Task WinningDogScoresOnlyInTheDogPool()
    {
        var seeded = await SeedLockedWeek(withDog: true);
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[0], 7, 14));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[1], 1, 0));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[2], 1, 0));

        await Run(service => service.FinalizeAsync(Admin, seeded.Week));

        var season = (await Run(service => service.GetAsync(seeded.Week))).Value!.SeasonId;
        var points = await Run(async (service, database) =>
        {
            var standings = new StandingsService(database);
            return new[] { StandingPool.Dog, StandingPool.Main, StandingPool.RegularSeason }
                .ToDictionary(pool => pool, pool => standings.GetAsync(season, pool).Result.Single().Points);
        });
        var dogLine = Assert.Single(Assert.Single(await LoadScores(seeded.Week)).Lines, line => line.Source == ScoreSource.DogPick);
        Assert.Equal(3, dogLine.Points);
        Assert.Equal(3, points[StandingPool.Dog]);
        Assert.Equal(2, points[StandingPool.Main]);
        Assert.Equal(2, points[StandingPool.RegularSeason]);
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 0)]
    public async Task RuleSeventeenFromTheSeasonDecidesWhetherATeamThatIsAlsoTheDogScoresAsADog(bool teamMayAlsoBeDog, int expectedDogPoints)
    {
        var season = new SeasonId(Guid.NewGuid());
        await SeedSeason(season, teamMayAlsoBeDog);
        var seeded = await SeedLockedWeek(withDog: true, season: season, dogTeamIsAlsoPicked: true);
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[0], 7, 14));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[1], 1, 0));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[2], 1, 0));

        await Run(service => service.FinalizeAsync(Admin, seeded.Week));

        var lines = Assert.Single(await LoadScores(seeded.Week)).Lines;
        Assert.Equal(expectedDogPoints, Assert.Single(lines, line => line.Source == ScoreSource.DogPick).Points);
        // The team pick itself is unaffected by R17 (the away team won game 0, so its point value counts either way).
        Assert.Equal(1, lines.Count(line => line.Source == ScoreSource.TeamPick && line.Points > 0 && line.TeamId == lines.Single(dog => dog.Source == ScoreSource.DogPick).TeamId));
    }

    [Fact]
    public async Task ChangingRuleSeventeenAfterAWeekIsFinalDoesNotRescoreItButARecomputeUsesTheCurrentRule()
    {
        var season = new SeasonId(Guid.NewGuid());
        await SeedSeason(season, teamMayAlsoBeDog: true);
        var seeded = await SeedLockedWeek(withDog: true, season: season, dogTeamIsAlsoPicked: true);
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[0], 7, 14));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[1], 1, 0));
        await Run(service => service.ConfirmResultAsync(Admin, seeded.Week, seeded.Games[2], 1, 0));
        await Run(service => service.FinalizeAsync(Admin, seeded.Week));
        Assert.Equal(3, Assert.Single(Assert.Single(await LoadScores(seeded.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);

        await using (var database = CreateDatabase())
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""update league.seasons set "Settings" = replace("Settings", '|True|', '|False|') where "Id" = {season.Value}""");
        }

        // No automatic re-score of the completed week...
        Assert.Equal(3, Assert.Single(Assert.Single(await LoadScores(seeded.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);
        // ...but an explicit recompute applies the rule as it now stands.
        Assert.True((await Run(service => service.RecomputeAsync(Admin, seeded.Week))).IsSuccess);
        Assert.Equal(0, Assert.Single(Assert.Single(await LoadScores(seeded.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);
    }

    [Fact]
    public async Task SeasonRecomputeCorrectsOnlyFinalRegularWeeksAndIsIdempotent()
    {
        var season = new SeasonId(Guid.NewGuid());
        await SeedSeason(season);
        var first = await SeedLockedWeek(withDog: true, season: season, number: 1, final: true);
        var second = await SeedLockedWeek(withDog: true, season: season, number: 2, final: true);
        var playoff = await SeedLockedWeek(WeekKind.Playoff, withDog: true, season: season, number: 19, final: true);
        var locked = await SeedLockedWeek(withDog: true, season: season, number: 3);
        var otherSeason = await SeedLockedWeek(withDog: true, number: 1, final: true);

        var summary = await Run(service => service.RecomputeSeasonAsync(Admin, season));
        var repeat = await Run(service => service.RecomputeSeasonAsync(Admin, season));

        Assert.True(summary.IsSuccess);
        Assert.Equal([1, 2], summary.Value!.Recomputed.Select(week => week.Number));
        Assert.Empty(summary.Value.Failed);
        Assert.Equal([1, 2], repeat.Value!.Recomputed.Select(week => week.Number));
        foreach (var week in new[] { first, second })
        {
            var score = Assert.Single(await LoadScores(week.Week));
            Assert.Equal(4, score.Lines.Count);
            Assert.Equal(3, Assert.Single(score.Lines, line => line.Source == ScoreSource.DogPick).Points);
        }

        // Playoff, locked and other-season weeks are left exactly as they were.
        Assert.Equal(0, Assert.Single(Assert.Single(await LoadScores(playoff.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);
        Assert.Empty(await LoadScores(locked.Week));
        Assert.Equal(0, Assert.Single(Assert.Single(await LoadScores(otherSeason.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);
        Assert.Equal(4, await Run((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "week.recomputed")));
        Assert.Equal(4, await Run((service, database) => database.WeekScores.CountAsync()));
    }

    [Fact]
    public async Task SeasonRecomputeReportsEmptySeasonsAndMissingSeasonsAndKeepsGoingPastABadWeek()
    {
        var empty = new SeasonId(Guid.NewGuid());
        await SeedSeason(empty);
        var season = new SeasonId(Guid.NewGuid());
        await SeedSeason(season);
        var bad = await SeedLockedWeek(withDog: true, season: season, number: 1, final: true);
        var good = await SeedLockedWeek(withDog: true, season: season, number: 2, final: true);
        await using (var database = CreateDatabase())
        {
            database.Ballots.Add(new Ballot(new BallotId(Guid.NewGuid()), bad.Membership, bad.Week));
            await database.SaveChangesAsync();
        }

        var none = await Run(service => service.RecomputeSeasonAsync(Admin, empty));
        var missing = await Run(service => service.RecomputeSeasonAsync(Admin, new SeasonId(Guid.NewGuid())));
        var mixed = await Run(service => service.RecomputeSeasonAsync(Admin, season));

        Assert.True(none.IsSuccess);
        Assert.Empty(none.Value!.Recomputed);
        Assert.Equal("season.missing", missing.Error!.Code);
        Assert.Equal([2], mixed.Value!.Recomputed.Select(week => week.Number));
        var failure = Assert.Single(mixed.Value.Failed);
        Assert.Equal((1, "week.ballots"), (failure.Number, failure.Code));
        Assert.Equal(3, Assert.Single(Assert.Single(await LoadScores(good.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);
        Assert.Equal(0, Assert.Single(Assert.Single(await LoadScores(bad.Week)).Lines, line => line.Source == ScoreSource.DogPick).Points);
    }

    [Fact]
    public async Task GenerateDogOptionsPersistsHomePerspectiveUnderdogsAndAudits()
    {
        var seeded = await SeedDraftWeek(3m, -3m, 0m, 7m); // pick'em game 2 yields no option

        var generated = await Run(service => service.GenerateDogOptionsAsync(Admin, seeded.Week));

        Assert.True(generated.IsSuccess);
        var expected = new[] { seeded.Games[0].Home, seeded.Games[1].Away, seeded.Games[3].Home }.Select(team => team.Value).Order();
        Assert.Equal(expected, (await Run(service => service.GetAsync(seeded.Week))).Value!.DogOptions.Select(team => team.Value).Order());
        Assert.Equal(["week.dog-options.generated"], await Run((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task RejectedGenerationAndReplacementLeaveThePersistedListAndAuditUntouched()
    {
        var seeded = await SeedDraftWeek(3m, null);
        var original = new[] { seeded.Games[0].Away };
        await Run(service => service.ReplaceDogOptionsAsync(Admin, seeded.Week, original));

        var partial = await Run(service => service.GenerateDogOptionsAsync(Admin, seeded.Week));
        var duplicate = await Run(service => service.ReplaceDogOptionsAsync(Admin, seeded.Week, [seeded.Games[0].Home, seeded.Games[0].Home]));
        var outside = await Run(service => service.ReplaceDogOptionsAsync(Admin, seeded.Week, [new TeamId(Guid.NewGuid())]));
        var missing = await Run(service => service.GenerateDogOptionsAsync(Admin, new WeekId(Guid.NewGuid())));

        Assert.Equal("week.lines", partial.Error!.Code);
        Assert.Equal("week.dogs", duplicate.Error!.Code);
        Assert.Equal("week.dogs", outside.Error!.Code);
        Assert.Equal("week.missing", missing.Error!.Code);
        Assert.Equal(original, (await Run(service => service.GetAsync(seeded.Week))).Value!.DogOptions);
        Assert.Equal(["week.dog-options.replaced"], await Run((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task AdminCanReplaceWithEitherSideAndDogOptionsFreezeOncePublished()
    {
        var seeded = await SeedDraftWeek(3m, -3m);
        await Run(service => service.GenerateDogOptionsAsync(Admin, seeded.Week));

        var replaced = await Run(service => service.ReplaceDogOptionsAsync(Admin, seeded.Week, [seeded.Games[0].Away, seeded.Games[1].Home]));
        await Run(service => service.ApproveLinesAsync(Admin, seeded.Week));
        await Run(service => service.PublishAsync(Admin, seeded.Week));
        var frozenReplace = await Run(service => service.ReplaceDogOptionsAsync(Admin, seeded.Week, [seeded.Games[0].Home]));
        var frozenGenerate = await Run(service => service.GenerateDogOptionsAsync(Admin, seeded.Week));

        Assert.True(replaced.IsSuccess);
        Assert.Equal("week.state", frozenReplace.Error!.Code);
        Assert.Equal("week.state", frozenGenerate.Error!.Code);
        Assert.Equal(new[] { seeded.Games[0].Away, seeded.Games[1].Home }.Select(team => team.Value).Order(), (await Run(service => service.GetAsync(seeded.Week))).Value!.DogOptions.Select(team => team.Value).Order());
    }

    private sealed record DraftGame(GameId Id, TeamId Home, TeamId Away);
    private sealed record DraftWeek(WeekId Week, List<DraftGame> Games);

    // A null spread leaves that game without an approved line.
    private async Task<DraftWeek> SeedDraftWeek(params decimal?[] spreads)
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var week = new Week(new WeekId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), 1, new WeekType(WeekKind.Regular, null, true));
        var games = new List<DraftGame>();
        foreach (var spread in spreads)
        {
            var game = new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow.AddDays(3)));
            week.AddGame(game);
            if (spread is { } value)
            {
                Assert.True(week.AddLine(game.Id, new Spread(value)).IsSuccess);
                Assert.True(week.ApproveLatestLine(game.Id).IsSuccess);
            }

            games.Add(new DraftGame(game.Id, game.HomeTeamId, game.AwayTeamId));
        }

        database.Weeks.Add(week);
        await database.SaveChangesAsync();
        return new DraftWeek(week.Id, games);
    }

    private sealed record LockedWeek(WeekId Week, List<GameId> Games, MembershipId Membership);

    private async Task SeedSeason(SeasonId season, bool teamMayAlsoBeDog = true)
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        // Live, because only one season may be in Setup at a time.
        var created = Season.CreateBlank(season, 2026, "Test season", isSimulation: false).Value!;
        Assert.True(created.SaveLeagueRules(LeagueRules.Default with { TeamMayAlsoBeDog = teamMayAlsoBeDog }).IsSuccess);
        Assert.True(created.ActivateWithDogs().IsSuccess);
        database.Seasons.Add(created);
        await database.SaveChangesAsync();
    }

    private async Task<LockedWeek> SeedLockedWeek(WeekKind kind = WeekKind.Regular, bool withDog = false, SeasonId? season = null, int number = 1, bool final = false, bool dogTeamIsAlsoPicked = false)
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var week = new Week(new WeekId(Guid.NewGuid()), season ?? new SeasonId(Guid.NewGuid()), number, new WeekType(kind, kind == WeekKind.Regular ? null : "WC", true));
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), week.Id);
        var games = new List<GameId>();
        for (var index = 0; index < 3; index++)
        {
            var game = new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow.AddDays(3)));
            week.AddGame(game);
            games.Add(game.Id);
            Assert.True(week.AddLine(game.Id, new Spread(-3m)).IsSuccess);
            Assert.True(week.ApproveLatestLine(game.Id).IsSuccess);
            // With dogTeamIsAlsoPicked the first pick is the same away team that is also the week's dog (R17).
            var picked = dogTeamIsAlsoPicked && index == 0 ? game.AwayTeamId : game.HomeTeamId;
            Assert.True(ballot.AddPick(new TeamPick(picked, game.Id, DateTimeOffset.UtcNow.AddDays(2)), DateTimeOffset.UtcNow).IsSuccess);
            if (withDog && index == 0) Assert.True(ballot.AddDog(new DogPick(game.AwayTeamId, game.Id, DateTimeOffset.UtcNow.AddDays(2)), false, DateTimeOffset.UtcNow).IsSuccess);
        }

        Assert.True(week.ApproveLines().IsSuccess);
        Assert.True(week.Publish().IsSuccess);
        Assert.True(week.Lock().IsSuccess);
        database.Weeks.Add(week);
        database.Ballots.Add(ballot);
        if (final)
        {
            // Final with the scores the pre-Phase-2.4 build stored: the winning dog (game 0 away) was zeroed.
            Assert.True(week.ConfirmResult(games[0], new GameResult(7, 14)).IsSuccess);
            foreach (var game in games.Skip(1)) Assert.True(week.ConfirmResult(game, new GameResult(1, 0)).IsSuccess);
            Assert.True(week.FinalizeResults().IsSuccess);
            var legacy = new WeekScore(new WeekScoreId(Guid.NewGuid()), week.SeasonId, ballot.MembershipId, week.Id);
            legacy.ReplaceLines([.. games.Select(game => new ScoreLine(new TeamId(Guid.NewGuid()), 0, ScoreSource.TeamPick, game.Value.ToString())), new ScoreLine(new TeamId(Guid.NewGuid()), 0, ScoreSource.DogPick, games[0].Value.ToString())]);
            database.WeekScores.Add(legacy);
        }

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
