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

public sealed class RosterDraftServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly List<Team> teams = Enumerable.Range(0, 32).Select(i => new Team(new TeamId(Guid.NewGuid()), $"Team {i:D2}", null, DateTimeOffset.UtcNow, $"Division {i / 4}")).ToList();
    private readonly UserId commissioner = new(Guid.NewGuid());
    private readonly UserId coachA = new(Guid.NewGuid());
    private readonly UserId coachB = new(Guid.NewGuid());
    private readonly UserId otherAdmin = new(Guid.NewGuid());
    private readonly UserId plainAdmin = new(Guid.NewGuid());
    private SeasonId seasonId;
    private MembershipId membershipA;
    private MembershipId membershipB;
    private DateTimeOffset deadline;

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    private TeamId T(int index) => teams[index].Id;
    private Guid[] Nine(int from = 0) => teams.Skip(from).Take(9).Select(team => team.Id.Value).ToArray();

    // A live season in its roster draft: a commissioner, two coaches, 32 teams, and an older live season with its own commissioner (the "unrelated admin").
    private async Task SeedAsync(bool coachBMembershipActive = true)
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        database.Teams.AddRange(teams);
        foreach (var (user, name) in new[] { (commissioner, "Commissioner"), (coachA, "Coach A"), (coachB, "Coach B"), (otherAdmin, "Other Admin"), (plainAdmin, "Plain Admin") })
            database.Users.Add(new User(user, Email.Create($"{name.Replace(' ', '.').ToLowerInvariant()}@example.com").Value!, name, name.Contains("Coach") ? UserRole.Coach : UserRole.Admin));

        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Swan League", false).Value!;
        season.SaveDoubleDogWeeks([2, 12, 17]);
        season.CompleteSetup();
        deadline = clock.UtcNow.AddDays(3);
        Assert.True(season.StartRosterDraft(deadline, clock.UtcNow, true).IsSuccess);
        seasonId = season.Id;
        database.Seasons.Add(season);
        database.SeasonTeams.AddRange(teams.Select((team, index) => new SeasonTeam(new SeasonTeamId(Guid.NewGuid()), season.Id, team.Id, team.Division, index % 4 + 1, 0)));
        membershipA = new MembershipId(Guid.NewGuid());
        membershipB = new MembershipId(Guid.NewGuid());
        database.Memberships.Add(new Membership(new MembershipId(Guid.NewGuid()), commissioner, season.Id, MembershipRole.Commissioner));
        database.Memberships.Add(new Membership(membershipA, coachA, season.Id, MembershipRole.Coach));
        var b = new Membership(membershipB, coachB, season.Id, MembershipRole.Coach);
        if (!coachBMembershipActive) b.Deactivate();
        database.Memberships.Add(b);

        var older = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2025, "2025 Swan League", false).Value!;
        older.ActivateWithDogs();
        database.Seasons.Add(older);
        database.Memberships.Add(new Membership(new MembershipId(Guid.NewGuid()), otherAdmin, older.Id, MembershipRole.Commissioner));
        await database.SaveChangesAsync();
    }

    [Fact]
    public async Task TheCoachDeskStartsEmptyWithEverySeasonTeamAndTheEasternDeadline()
    {
        await SeedAsync();

        var desk = (await Coach(service => service.GetAsync(coachA))).Value!;

        Assert.Equal(("open", "2026 Swan League", false, false, 0), (desk.State, desk.SeasonName, desk.IsLocked, desk.Complete, desk.Revision));
        Assert.Equal(32, desk.Teams.Count);
        Assert.DoesNotContain(desk.Teams, team => team.Selected);
        Assert.Equal(deadline, desk.Deadline);
        Assert.Equal("2026-10-18T08:00", desk.DeadlineEastern);
        Assert.Equal(clock.UtcNow, desk.ServerTime);
        Assert.Equal(0, await Coach((service, database) => database.Rosters.CountAsync()));
    }

    [Fact]
    public async Task ACoachSavesExactlyNineTeamsRevisesThemAndEachChangeIsAudited()
    {
        await SeedAsync();

        var first = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        var revised = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 1, Nine(10))));
        var same = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 2, Nine(10).Reverse().ToArray())));

        Assert.True(first.IsSuccess);
        Assert.Equal((2, true, 9), (revised.Value!.Revision, revised.Value.Complete, revised.Value.Teams.Count(team => team.Selected)));
        Assert.Equal(Nine(10).Order(), revised.Value.Teams.Where(team => team.Selected).Select(team => team.TeamId).Order());
        Assert.Equal(2, same.Value!.Revision);
        Assert.Equal(["roster.saved", "roster.saved"], await Coach((service, database) => database.AuditEntries.OrderBy(entry => entry.At).Select(entry => entry.Action).ToListAsync()));
        Assert.Equal(1, await Coach((service, database) => database.Rosters.CountAsync()));
    }

    [Theory]
    [InlineData("eight")]
    [InlineData("ten")]
    [InlineData("none")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task WrongCountsDuplicatesAndUnknownTeamsAreRefusedAndTheSavedRosterSurvives(string kind)
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        Guid[] attempt = kind switch
        {
            "eight" => Nine(10).Take(8).ToArray(),
            "ten" => teams.Skip(10).Take(10).Select(team => team.Id.Value).ToArray(),
            "none" => [],
            "duplicate" => Nine(10).Take(8).Append(Nine(10)[0]).ToArray(),
            _ => Nine(10).Take(8).Append(Guid.NewGuid()).ToArray(),
        };

        var refused = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 1, attempt)));

        Assert.Equal(kind switch { "duplicate" => "roster.duplicate", "unknown" => "roster.team", _ => "roster.count" }, refused.Error!.Code);
        var desk = (await Coach(service => service.GetAsync(coachA))).Value!;
        Assert.Equal(1, desk.Revision);
        Assert.Equal(Nine(0).Order(), desk.Teams.Where(team => team.Selected).Select(team => team.TeamId).Order());
    }

    [Fact]
    public async Task AStaleRevisionOrAWrongSeasonIsRefusedWithoutSaving()
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));

        var stale = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(5))));
        var wrong = await Coach(service => service.SaveAsync(coachA, new SaveRoster(Guid.NewGuid(), 1, Nine(5))));

        Assert.Equal("roster.stale", stale.Error!.Code);
        Assert.Equal("season.stale", wrong.Error!.Code);
        Assert.Equal(1, (await Coach(service => service.GetAsync(coachA))).Value!.Revision);
    }

    [Fact]
    public async Task LockingNeedsNineTeamsTakesEffectAtOnceAndCannotBeRepeated()
    {
        await SeedAsync();

        var empty = await Coach(service => service.LockAsync(coachA, new LockRoster(seasonId.Value, 0)));
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        var locked = await Coach(service => service.LockAsync(coachA, new LockRoster(seasonId.Value, 1)));
        var again = await Coach(service => service.LockAsync(coachA, new LockRoster(seasonId.Value, 2)));
        var edit = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 2, Nine(5))));

        Assert.Equal("roster.count", empty.Error!.Code);
        Assert.Equal(("locked", true, 2, clock.UtcNow), (locked.Value!.State, locked.Value.IsLocked, locked.Value.Revision, locked.Value.LockedAt!.Value));
        Assert.Equal("roster.locked", again.Error!.Code);
        Assert.Equal("roster.locked", edit.Error!.Code);
        Assert.Equal(Nine(0).Order(), (await Coach(service => service.GetAsync(coachA))).Value!.Teams.Where(team => team.Selected).Select(team => team.TeamId).Order());
        Assert.Equal(["roster.saved", "roster.locked"], await Coach((service, database) => database.AuditEntries.OrderBy(entry => entry.At).Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task AfterTheDeadlineNothingCanBeSavedOrLockedAndAnIncompleteRosterStaysIncomplete()
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        clock.UtcNow = deadline;

        var late = await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 1, Nine(5))));
        var lateLock = await Coach(service => service.LockAsync(coachA, new LockRoster(seasonId.Value, 1)));
        var never = await Coach(service => service.SaveAsync(coachB, new SaveRoster(seasonId.Value, 0, Nine(0))));
        var deskA = (await Coach(service => service.GetAsync(coachA))).Value!;
        var deskB = (await Coach(service => service.GetAsync(coachB))).Value!;

        Assert.Equal(["roster.locked", "roster.locked", "roster.locked"], new[] { late.Error!.Code, lateLock.Error!.Code, never.Error!.Code });
        Assert.Equal(("closed", true, 1), (deskA.State, deskA.Complete, deskA.Revision));
        Assert.Equal(("closed", false, 0, 0), (deskB.State, deskB.Complete, deskB.Revision, deskB.Teams.Count(team => team.Selected)));
        Assert.Equal(1, await Coach((service, database) => database.Rosters.CountAsync()));
    }

    [Fact]
    public async Task OnlyAnActiveCoachOfALiveSeasonHasADesk()
    {
        await SeedAsync(coachBMembershipActive: false);

        foreach (var user in new[] { coachB, commissioner, plainAdmin, new UserId(Guid.NewGuid()) })
        {
            Assert.Equal("no-membership", (await Coach(service => service.GetAsync(user))).Value!.State);
            Assert.Equal("roster.unavailable", (await Coach(service => service.SaveAsync(user, new SaveRoster(seasonId.Value, 0, Nine(0))))).Error!.Code);
            Assert.Equal("roster.unavailable", (await Coach(service => service.LockAsync(user, new LockRoster(seasonId.Value, 0)))).Error!.Code);
        }

        Assert.Equal(0, await Coach((service, database) => database.Rosters.CountAsync()));
    }

    [Fact]
    public async Task CoachesNeverTouchEachOthersRosters()
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));

        var deskB = (await Coach(service => service.GetAsync(coachB))).Value!;
        var savedB = await Coach(service => service.SaveAsync(coachB, new SaveRoster(seasonId.Value, 0, Nine(5))));

        Assert.DoesNotContain(deskB.Teams, team => team.Selected);
        Assert.True(savedB.IsSuccess);
        Assert.Equal(Nine(0).Order(), (await Coach(service => service.GetAsync(coachA))).Value!.Teams.Where(team => team.Selected).Select(team => team.TeamId).Order());
        Assert.Equal(2, await Coach((service, database) => database.Rosters.CountAsync()));
    }

    [Fact]
    public async Task TwoSavesFromTheSameScreenCannotBothWin()
    {
        await SeedAsync();

        var results = await Task.WhenAll(
            Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0)))),
            Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(10)))));

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.Equal(["roster.stale"], results.Where(result => !result.IsSuccess).Select(result => result.Error!.Code));
        Assert.Equal(1, await Coach((service, database) => database.Rosters.CountAsync()));
    }

    [Fact]
    public async Task TheDatabaseAllowsOneRosterPerCoachAndSeason()
    {
        await SeedAsync();
        await using var first = CreateDatabase();
        first.Rosters.Add(new Roster(new RosterId(Guid.NewGuid()), seasonId, membershipA));
        await first.SaveChangesAsync();
        await using var second = CreateDatabase();
        second.Rosters.Add(new Roster(new RosterId(Guid.NewGuid()), seasonId, membershipA));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task TheCommissionerSeesEveryCoachWithMissingAndIncompleteRostersFirst()
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        await Coach(service => service.LockAsync(coachA, new LockRoster(seasonId.Value, 1)));

        var draft = (await Commissioner(service => service.GetAsync(commissioner))).Value!;

        Assert.Equal(("2026 Swan League", true, 32), (draft.SeasonName, draft.DraftOpen, draft.Teams.Count));
        Assert.Equal([("Coach B", "missing"), ("Coach A", "locked")], draft.Coaches.Select(coach => (coach.Name, coach.Status)));
        var a = draft.Coaches.Single(coach => coach.Name == "Coach A");
        Assert.Equal((9, 9, true), (a.TeamCount, a.Teams.Count, a.IsLocked));
    }

    [Theory]
    [InlineData("otherAdmin")]
    [InlineData("plainAdmin")]
    [InlineData("coach")]
    [InlineData("nobody")]
    public async Task OnlyTheSeasonsOwnCommissionerMayManageRosters(string who)
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        var actor = who switch { "otherAdmin" => otherAdmin, "plainAdmin" => plainAdmin, "coach" => coachA, _ => new UserId(Guid.NewGuid()) };

        var read = await Commissioner(service => service.GetAsync(actor));
        var correct = await Commissioner(service => service.CorrectAsync(actor, new CorrectRoster(seasonId.Value, membershipA.Value, 1, T(0).Value, T(20).Value, "Because")));

        Assert.Equal("commissioner.required", read.Error!.Code);
        Assert.Equal("commissioner.required", correct.Error!.Code);
        Assert.Equal(Nine(0).Order(), (await Coach(service => service.GetAsync(coachA))).Value!.Teams.Where(team => team.Selected).Select(team => team.TeamId).Order());
        Assert.DoesNotContain("roster.corrected", await Coach((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task AfterTheDeadlineTheCommissionerReplacesATeamWithAReasonAndItIsAudited()
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        await Coach(service => service.LockAsync(coachA, new LockRoster(seasonId.Value, 1)));
        clock.UtcNow = deadline.AddHours(2);

        var corrected = await Commissioner(service => service.CorrectAsync(commissioner, new CorrectRoster(seasonId.Value, membershipA.Value, 2, T(0).Value, T(20).Value, "  Coach picked the wrong team  ")));

        Assert.True(corrected.IsSuccess, corrected.Error?.Message);
        var a = corrected.Value!.Coaches.Single(coach => coach.Name == "Coach A");
        Assert.Contains(T(20).Value, a.TeamIds);
        Assert.DoesNotContain(T(0).Value, a.TeamIds);
        Assert.Equal((9, "locked", 3), (a.TeamCount, a.Status, a.Revision));
        var entry = Assert.Single(await Coach((service, database) => database.AuditEntries.Where(item => item.Action == "roster.corrected").ToListAsync()));
        Assert.Equal((commissioner, clock.UtcNow), (entry.UserId, entry.At));
        Assert.Contains("Coach picked the wrong team", entry.Details);
        Assert.Contains(T(0).Value.ToString(), entry.Details);
        Assert.Contains(T(20).Value.ToString(), entry.Details);
    }

    [Fact]
    public async Task TheSameCorrectionWorksBeforeTheDeadlineAndCanCompleteAnIncompleteRoster()
    {
        await SeedAsync();

        var first = await Commissioner(service => service.CorrectAsync(commissioner, new CorrectRoster(seasonId.Value, membershipB.Value, 0, null, T(3).Value, "Never drafted")));
        var second = await Commissioner(service => service.CorrectAsync(commissioner, new CorrectRoster(seasonId.Value, membershipB.Value, 1, null, T(4).Value, "Never drafted")));

        Assert.True(first.IsSuccess && second.IsSuccess);
        var b = second.Value!.Coaches.Single(coach => coach.Name == "Coach B");
        Assert.Equal(("incomplete", 2, 2), (b.Status, b.TeamCount, b.Revision));
        Assert.Equal(2, await Coach((service, database) => database.AuditEntries.CountAsync(entry => entry.Action == "roster.corrected")));
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("notOnRoster")]
    [InlineData("alreadyOnRoster")]
    [InlineData("unknownTeam")]
    [InlineData("full")]
    [InlineData("stale")]
    [InlineData("noCoach")]
    public async Task AnInvalidCorrectionChangesNothingAndWritesNoAudit(string kind)
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));
        var request = kind switch
        {
            "blank" => new CorrectRoster(seasonId.Value, membershipA.Value, 1, T(0).Value, T(20).Value, "   "),
            "notOnRoster" => new CorrectRoster(seasonId.Value, membershipA.Value, 1, T(25).Value, T(20).Value, "Because"),
            "alreadyOnRoster" => new CorrectRoster(seasonId.Value, membershipA.Value, 1, T(0).Value, T(1).Value, "Because"),
            "unknownTeam" => new CorrectRoster(seasonId.Value, membershipA.Value, 1, T(0).Value, Guid.NewGuid(), "Because"),
            "full" => new CorrectRoster(seasonId.Value, membershipA.Value, 1, null, T(20).Value, "Because"),
            "stale" => new CorrectRoster(seasonId.Value, membershipA.Value, 0, T(0).Value, T(20).Value, "Because"),
            _ => new CorrectRoster(seasonId.Value, Guid.NewGuid(), 0, null, T(20).Value, "Because"),
        };

        var refused = await Commissioner(service => service.CorrectAsync(commissioner, request));

        Assert.Equal(kind switch { "blank" => "roster.reason", "notOnRoster" => "roster.correction.invalid", "alreadyOnRoster" => "roster.duplicate", "unknownTeam" => "roster.team", "full" => "roster.count", "stale" => "roster.stale", _ => "coach.missing" }, refused.Error!.Code);
        Assert.Equal(Nine(0).Order(), (await Coach(service => service.GetAsync(coachA))).Value!.Teams.Where(team => team.Selected).Select(team => team.TeamId).Order());
        Assert.DoesNotContain("roster.corrected", await Coach((service, database) => database.AuditEntries.Select(entry => entry.Action).ToListAsync()));
    }

    [Fact]
    public async Task ACorrectionAndTheCoachsOwnSaveTakeTurns()
    {
        await SeedAsync();
        await Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 0, Nine(0))));

        var correction = Commissioner(service => service.CorrectAsync(commissioner, new CorrectRoster(seasonId.Value, membershipA.Value, 1, T(0).Value, T(20).Value, "Because")));
        var save = Coach(service => service.SaveAsync(coachA, new SaveRoster(seasonId.Value, 1, Nine(10))));
        await Task.WhenAll(correction, save);

        Assert.Equal(1, new[] { (await correction).IsSuccess, (await save).IsSuccess }.Count(success => success));
        Assert.Equal(2, (await Commissioner(service => service.GetAsync(commissioner))).Value!.Coaches.Single(coach => coach.Name == "Coach A").Revision);
    }

    private Task<T> Coach<T>(Func<CoachRosterService, Task<T>> action) => Coach((service, _) => action(service));

    private async Task<T> Coach<T>(Func<CoachRosterService, SwanDbContext, Task<T>> action)
    {
        await using var database = CreateDatabase();
        return await action(new CoachRosterService(database, clock, new AuditService(database, clock)), database);
    }

    private async Task<T> Commissioner<T>(Func<CommissionerRosterService, Task<T>> action)
    {
        await using var database = CreateDatabase();
        return await action(new CommissionerRosterService(database, clock, new AuditService(database, clock)));
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
