using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Picks;
using Swan.Services.Platform;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class CoachPickServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly MutableClock clock = new(DateTimeOffset.UtcNow);
    private readonly List<Team> teams = Enumerable.Range(0, 12).Select(i => new Team(new TeamId(Guid.NewGuid()), $"Team {i:D2}", null, DateTimeOffset.UtcNow, "AFC East")).ToList();
    private readonly UserId coachA = new(Guid.NewGuid());
    private readonly UserId coachB = new(Guid.NewGuid());
    private SeasonId seasonId;
    private WeekId weekId;
    private readonly List<GameId> games = [];

    public ValueTask InitializeAsync() => new(postgres.StartAsync());

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    private TeamId T(int index) => teams[index].Id;

    // Coach A's roster is teams 0-8, coach B's is 3-11. Week games: T0 (home, -3) v T9, T1 (home, +2.5) v T10, T11 (away, -7 at home T2) i.e. T2 v T11, T3 v T4 (pick'em); T5 has a bye.
    private async Task SeedAsync(int weekNumber = 4, bool openWeek = true, bool coachARoster = true, bool coachAMembership = true, int coachATeams = 9)
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Swan League", false).Value!;
        season.ActivateWithDogs();
        seasonId = season.Id;
        database.Seasons.Add(season);
        database.Teams.AddRange(teams);
        database.SeasonTeams.AddRange(teams.Select((team, index) => new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), season.Id, team.Id, $"Division {index / 4}", index % 4 + 1, 0)));
        database.Users.AddRange(new User(coachA, Email.Create("a@example.com").Value!, "Coach A"), new User(coachB, Email.Create("b@example.com").Value!, "Coach B"));
        var membershipA = new Membership(new MembershipId(Guid.NewGuid()), coachA, season.Id, coachAMembership ? MembershipRole.Coach : MembershipRole.Administrator);
        var membershipB = new Membership(new MembershipId(Guid.NewGuid()), coachB, season.Id, MembershipRole.Coach);
        database.Memberships.AddRange(membershipA, membershipB);
        if (coachARoster) database.Rosters.Add(Roster(season.Id, membershipA.Id, Enumerable.Range(0, coachATeams)));
        database.Rosters.Add(Roster(season.Id, membershipB.Id, Enumerable.Range(3, 9)));

        var week = new Week(new WeekId(Guid.NewGuid()), season.Id, weekNumber, new WeekType(WeekKind.Regular, null, true));
        weekId = week.Id;
        var start = clock.UtcNow.AddDays(1);
        (int Home, int Away, decimal Line, int Days)[] fixtures = [(0, 9, -3m, 1), (1, 10, 2.5m, 2), (2, 11, -7m, 3), (3, 4, 0m, 3)];
        foreach (var (home, away, line, days) in fixtures)
        {
            var game = new Game(new GameId(Guid.NewGuid()), T(home), T(away), new Kickoff(start.AddDays(days - 1)));
            games.Add(game.Id);
            week.AddGame(game);
            week.AddLine(game.Id, Spread.Create(line).Value!);
            week.ApproveLatestLine(game.Id);
        }

        Assert.True(week.GenerateDogOptions().IsSuccess);
        Assert.True(week.ApproveLines().IsSuccess);
        if (openWeek) Assert.True(week.Publish().IsSuccess);
        database.Weeks.Add(week);
        await database.SaveChangesAsync();
    }

    private Roster Roster(SeasonId season, MembershipId membership, IEnumerable<int> indexes)
    {
        var roster = new Roster(new RosterId(Guid.NewGuid()), season, membership);
        foreach (var index in indexes) roster.AddTeam(T(index), windowOpen: true);
        return roster;
    }

    [Fact]
    public async Task TheDeskListsOnlyTheCoachsOwnTeamsThatPlayWithTheirPointValuesLocksAndTheOfferedDogs()
    {
        await SeedAsync();

        var desk = (await Run(service => service.GetAsync(coachA))).Value!;

        Assert.Equal(("open", "2026 Swan League", 0), (desk.State, desk.SeasonName, desk.Revision));
        Assert.Equal((4, 3, 1, false), (desk.Week!.Number, desk.Week.PickLimit, desk.Week.DogLimit, desk.Week.IsDoubleDogWeek));
        Assert.Equal(["Team 00", "Team 01", "Team 02", "Team 03", "Team 04"], desk.Games.Select(row => row.Team).Order());
        Assert.Equal(["Team 05", "Team 06", "Team 07", "Team 08"], desk.NotPlaying);
        var first = desk.Games.Single(row => row.Team == "Team 00");
        Assert.Equal(("Team 09", true, 1, false, false), (first.Opponent, first.IsHome, first.PointValue, first.Locked, first.Selected));
        Assert.True(first.LockTime <= first.Kickoff);
        Assert.DoesNotContain(desk.Games, row => row.Team is "Team 09" or "Team 10" or "Team 11");
        Assert.Equal(["Team 09", "Team 01", "Team 11"], desk.Dogs.Select(dog => dog.Team));
        Assert.Equal([3m, 2.5m, 7m], desk.Dogs.Select(dog => dog.Spread));
        Assert.All(desk.Dogs, dog => Assert.Equal(dog.Kickoff, dog.LockTime));
    }

    [Fact]
    public async Task TheDeskSaysWhyThereIsNothingToPick()
    {
        await SeedAsync(openWeek: false);
        Assert.Equal(("no-week", "2026 Swan League"), ((await Run(service => service.GetAsync(coachA))).Value!.State, (await Run(service => service.GetAsync(coachA))).Value!.SeasonName));
        Assert.Equal("no-membership", (await Run(service => service.GetAsync(new UserId(Guid.NewGuid())))).Value!.State);
    }

    [Fact]
    public async Task ACoachWithoutRosterTeamsOrAMembershipGetsAnHonestEmptyState()
    {
        await SeedAsync(coachARoster: false);
        var noRoster = (await Run(service => service.GetAsync(coachA))).Value!;
        var save = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [])));

        Assert.Equal(("no-roster", 0, 0), (noRoster.State, noRoster.Games.Count, noRoster.Dogs.Count));
        Assert.Equal("pick.unavailable", save.Error!.Code);
        Assert.Equal(0, await Run((service, database) => database.Ballots.CountAsync()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task ARosterWithFewerThanNineTeamsCannotMakeWeeklyPicksAndSaysSo(int teamCount)
    {
        await SeedAsync(coachATeams: teamCount);

        var desk = (await Run(service => service.GetAsync(coachA))).Value!;
        var save = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [])));

        Assert.Equal(("roster-incomplete", 0, 0), (desk.State, desk.Games.Count, desk.Dogs.Count));
        Assert.Equal("roster.incomplete", save.Error!.Code);
        Assert.Equal(0, await Run((service, database) => database.Ballots.CountAsync()));
    }

    [Fact]
    public async Task AnAdministratorMembershipIsNotACoachMembership()
    {
        await SeedAsync(coachAMembership: false);

        Assert.Equal("no-membership", (await Run(service => service.GetAsync(coachA))).Value!.State);
        Assert.Equal("pick.unavailable", (await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [])))).Error!.Code);
    }

    [Fact]
    public async Task SavedPicksAndDogsComeBackSelectedWithTheirPointValuesAndAreAudited()
    {
        await SeedAsync();

        var saved = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value, T(2).Value, T(4).Value], [T(9).Value])));
        var reloaded = (await Run(service => service.GetAsync(coachA))).Value!;

        Assert.True(saved.IsSuccess, saved.Error?.Code + ": " + saved.Error?.Message);
        Assert.Equal(1, reloaded.Revision);
        Assert.Equal(["Team 00", "Team 02", "Team 04"], reloaded.Games.Where(row => row.Selected).Select(row => row.Team).Order());
        Assert.Equal(["Team 09"], reloaded.Dogs.Where(dog => dog.Selected).Select(dog => dog.Team));
        var ballot = await Run((service, database) => database.Ballots.SingleAsync());
        Assert.Equal([1, 3, 1], ballot.Picks.OrderBy(pick => pick.TeamId == T(0) ? 0 : pick.TeamId == T(2) ? 1 : 2).Select(pick => pick.PointValue));
        Assert.Equal(["ballot.saved"], await Run((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task RevisingReplacesTheSelectionsAndRecordsTheChange()
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value, T(1).Value], [T(9).Value])));

        var revised = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, [T(2).Value], [T(1).Value])));

        Assert.Equal(2, revised.Value!.Revision);
        Assert.Equal(["Team 02"], revised.Value.Games.Where(row => row.Selected).Select(row => row.Team));
        Assert.Equal(["Team 01"], revised.Value.Dogs.Where(dog => dog.Selected).Select(dog => dog.Team));
        Assert.Single((await Run((service, database) => database.Ballots.SingleAsync())).Changes);
        Assert.Equal(2, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task SavingTheSameThingAgainChangesNothingAndWritesNoAudit()
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [])));

        var again = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, [T(0).Value], [])));

        Assert.Equal(1, again.Value!.Revision);
        Assert.Equal(1, await Run((service, database) => database.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task AStaleRevisionOrWeekIsRefusedAndTheSavedBallotSurvives()
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [])));

        var stale = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(1).Value], [])));
        var wrongWeek = await Run(service => service.SaveAsync(coachA, new SavePicks(Guid.NewGuid(), 1, [T(1).Value], [])));

        Assert.Equal("ballot.stale", stale.Error!.Code);
        Assert.Equal("week.stale", wrongWeek.Error!.Code);
        Assert.Equal(["Team 00"], (await Run(service => service.GetAsync(coachA))).Value!.Games.Where(row => row.Selected).Select(row => row.Team));
    }

    [Theory]
    [InlineData("stranger")]
    [InlineData("bye")]
    [InlineData("four")]
    [InlineData("twice")]
    public async Task InvalidTeamPicksAreRefusedWithoutTouchingTheSavedBallot(string kind)
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [T(9).Value])));
        Guid[] ids = kind switch
        {
            "stranger" => [T(9).Value],
            "bye" => [T(5).Value],
            "four" => [T(0).Value, T(1).Value, T(2).Value, T(3).Value],
            _ => [T(1).Value, T(1).Value],
        };

        var refused = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, ids, [T(9).Value])));

        Assert.Equal("pick.invalid", refused.Error!.Code);
        var desk = (await Run(service => service.GetAsync(coachA))).Value!;
        Assert.Equal((1, "Team 00"), (desk.Revision, desk.Games.Single(row => row.Selected).Team));
    }

    [Theory]
    [InlineData("notOffered")]
    [InlineData("two")]
    [InlineData("twice")]
    public async Task InvalidDogsAreRefusedWithoutTouchingTheSavedBallot(string kind)
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [T(9).Value])));
        Guid[] dogs = kind switch
        {
            "notOffered" => [T(0).Value],
            "two" => [T(9).Value, T(1).Value],
            _ => [T(9).Value, T(9).Value],
        };

        var refused = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, [T(0).Value], dogs)));

        Assert.Equal("dog.invalid", refused.Error!.Code);
        Assert.Equal(["Team 09"], (await Run(service => service.GetAsync(coachA))).Value!.Dogs.Where(dog => dog.Selected).Select(dog => dog.Team));
    }

    [Fact]
    public async Task ADoubleDogWeekAllowsTwoDogsFromTwoGames()
    {
        await SeedAsync(weekNumber: 2);

        var desk = (await Run(service => service.GetAsync(coachA))).Value!;
        var saved = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [], [T(9).Value, T(1).Value])));

        Assert.Equal((true, 2), (desk.Week!.IsDoubleDogWeek, desk.Week.DogLimit));
        Assert.True(saved.IsSuccess);
        Assert.Equal(2, saved.Value!.Dogs.Count(dog => dog.Selected));
    }

    [Fact]
    public async Task AGameThatHasLockedKeepsItsPickAndRefusesChanges()
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value, T(2).Value], [T(9).Value])));
        clock.UtcNow = clock.UtcNow.AddDays(1).AddHours(1);

        var desk = (await Run(service => service.GetAsync(coachA))).Value!;
        var removed = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, [T(2).Value], [T(9).Value])));
        var dogRemoved = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, [T(0).Value, T(2).Value], [])));
        var kept = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 1, [T(0).Value, T(3).Value], [T(9).Value])));

        Assert.True(desk.Games.Single(row => row.Team == "Team 00").Locked);
        Assert.False(desk.Games.Single(row => row.Team == "Team 02").Locked);
        Assert.Equal("pick.locked", removed.Error!.Code);
        Assert.Equal("dog.locked", dogRemoved.Error!.Code);
        Assert.True(kept.IsSuccess);
        Assert.Equal(["Team 00", "Team 03"], kept.Value!.Games.Where(row => row.Selected).Select(row => row.Team).Order());
    }

    [Fact]
    public async Task ANewPickForAGameThatAlreadyLockedIsRefused()
    {
        await SeedAsync();
        clock.UtcNow = clock.UtcNow.AddDays(1).AddHours(1);

        var refused = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [])));

        Assert.Equal("pick.locked", refused.Error!.Code);
        Assert.Equal(0, await Run((service, database) => database.Ballots.CountAsync()));
    }

    [Fact]
    public async Task ACoachNeverSeesOrChangesAnotherCoachsBallot()
    {
        await SeedAsync();
        await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], [T(9).Value])));

        var deskB = (await Run(service => service.GetAsync(coachB))).Value!;
        var savedB = await Run(service => service.SaveAsync(coachB, new SavePicks(weekId.Value, 0, [T(3).Value], [])));
        var stolen = await Run(service => service.SaveAsync(coachB, new SavePicks(weekId.Value, 1, [T(0).Value], [])));

        Assert.Equal(0, deskB.Revision);
        Assert.DoesNotContain(deskB.Games, row => row.Selected);
        Assert.DoesNotContain(deskB.Games, row => row.Team == "Team 00");
        Assert.True(savedB.IsSuccess);
        Assert.Equal("pick.invalid", stolen.Error!.Code);
        Assert.Equal(2, await Run((service, database) => database.Ballots.CountAsync()));
        Assert.Equal(["Team 00"], (await Run(service => service.GetAsync(coachA))).Value!.Games.Where(row => row.Selected).Select(row => row.Team));
    }

    [Fact]
    public async Task ACloseWeekCannotBeSavedTo()
    {
        await SeedAsync();
        await using (var database = CreateDatabase())
        {
            var week = await database.Weeks.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
            week.Lock();
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var save = await Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(1).Value], [])));

        Assert.Equal("pick.unavailable", save.Error!.Code);
        Assert.Equal("no-week", (await Run(service => service.GetAsync(coachA))).Value!.State);
    }

    [Fact]
    public async Task ASaveThatRacesTheAdminLockingTheWeekWaitsForItAndIsRefused()
    {
        await SeedAsync();
        // The admin's lock is in flight: written but not yet committed.
        await using var connection = new Npgsql.NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var adminTransaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var update = new Npgsql.NpgsqlCommand("""update schedule.weeks set "State" = @locked where "Id" = @id""", connection, adminTransaction))
        {
            update.Parameters.AddWithValue("locked", (int)WeekState.Locked);
            update.Parameters.AddWithValue("id", weekId.Value);
            Assert.Equal(1, await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        var save = Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(1).Value], [])));
        var finishedEarly = await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)) == save;
        await adminTransaction.CommitAsync(TestContext.Current.CancellationToken);
        var result = await save;

        Assert.False(finishedEarly, "The save must wait for the week row instead of reading the old Open state.");
        Assert.Equal("pick.unavailable", result.Error!.Code);
        Assert.Equal(0, await Run((service, database) => database.Ballots.CountAsync()));
    }

    [Fact]
    public async Task TwoSavesFromTheSameScreenCannotBothWin()
    {
        await SeedAsync();

        var results = await Task.WhenAll(
            Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(0).Value], []))),
            Run(service => service.SaveAsync(coachA, new SavePicks(weekId.Value, 0, [T(1).Value], []))));

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.Equal(["ballot.stale"], results.Where(result => !result.IsSuccess).Select(result => result.Error!.Code));
        Assert.Equal(1, await Run((service, database) => database.Ballots.CountAsync()));
        Assert.Single((await Run((service, database) => database.Ballots.SingleAsync())).Picks);
    }

    [Fact]
    public async Task TheDatabaseItselfAllowsOnlyOneBallotPerCoachAndWeek()
    {
        await SeedAsync();
        await using var database = CreateDatabase();
        var membership = await database.Memberships.FirstAsync(item => item.UserId == coachA, cancellationToken: TestContext.Current.CancellationToken);
        database.Ballots.Add(new Ballot(new BallotId(Guid.NewGuid()), membership.Id, weekId));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var second = CreateDatabase();
        second.Ballots.Add(new Ballot(new BallotId(Guid.NewGuid()), membership.Id, weekId));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RosterTeamsSurviveASaveAndReload()
    {
        await SeedAsync();

        await using var database = CreateDatabase();
        var roster = await database.Rosters.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([9, 9], roster.Select(item => item.Teams.Count));
    }

    private Task<T> Run<T>(Func<CoachPickService, Task<T>> action) => Run((service, _) => action(service));

    private async Task<T> Run<T>(Func<CoachPickService, SwanDbContext, Task<T>> action)
    {
        await using var database = CreateDatabase();
        return await action(new CoachPickService(database, clock, new AuditService(database, clock)), database);
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
