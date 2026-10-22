using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Core.League;
using Xunit;

namespace Swan.Api.Tests;

// Real authentication handler, real routes and a real database: who may create a season, and that a refusal leaves no trace.
public sealed class SeasonCreatorAuthorizationTests(SessionApiFixture fixture) : IClassFixture<SessionApiFixture>
{
    private const string Url = "/api/admin/seasons";
    private static readonly UserId SeededCoach = new(Guid.Parse("00000000-0000-0000-0000-000000000201"));

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private static HttpRequestMessage Post(string url, string? cookie, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { year = 2031, name = "2031 Swan League" }) };
        if (cookie is not null) request.Headers.Add("Cookie", $"swan_session={cookie}");
        return request;
    }

    private static HttpRequestMessage Get(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"swan_session={cookie}");
        return request;
    }

    private async Task<UserId> UserAsync(string email, UserRole role, bool commissioner = false, bool commissionerActive = true)
    {
        var id = new UserId(Guid.NewGuid());
        await fixture.WithDatabase(fixture.Development, async (database, _) =>
        {
            database.Users.Add(new User(id, Email.Create(email).Value!, email, role));
            if (commissioner)
            {
                var membership = new Membership(new MembershipId(Guid.NewGuid()), id, new SeasonId(Guid.Parse("3908b3ef-4b67-4cf8-890f-4d72a295c8aa")), MembershipRole.Commissioner);
                if (!commissionerActive) membership.Deactivate();
                database.Memberships.Add(membership);
            }

            await database.SaveChangesAsync();
            return 0;
        });
        return id;
    }

    private Task<string> SessionFor(UserId user) => fixture.WithDatabase(fixture.Development, async (_, sessions) => (await sessions.CreateAsync(user)).Credential);

    private async Task<(int Setup, int Memberships, int Audits)> Footprint() => await fixture.WithDatabase(fixture.Development, async (database, _) =>
        (await database.Seasons.CountAsync(season => season.State == SeasonState.Setup), await database.Memberships.CountAsync(), await database.AuditEntries.CountAsync(entry => entry.Action == "season.setup.created")));

    // Removes seasons created by these tests while retaining the fixture's 2026 session season.
    private Task RemoveSeasonsNewerThanTheSeededOne() => fixture.WithDatabase(fixture.Development, async (database, _) =>
    {
        await database.Database.ExecuteSqlRawAsync("""
            delete from schedule.games where "WeekId" in (select w."Id" from schedule.weeks w join league.seasons s on s."Id" = w."SeasonId" where s."Year" > 2026);
            delete from schedule.weeks where "SeasonId" in (select "Id" from league.seasons where "Year" > 2026);
            delete from schedule.season_teams where "SeasonId" in (select "Id" from league.seasons where "Year" > 2026);
            delete from picks.rosters where "SeasonId" in (select "Id" from league.seasons where "Year" > 2026);
            delete from league.memberships where "SeasonId" in (select "Id" from league.seasons where "Year" > 2026);
            delete from league.seasons where "Year" > 2026
            """);
        return 0;
    });

    private Task ClearSetup() => fixture.WithDatabase(fixture.Development, async (database, _) =>
    {
        await database.Database.ExecuteSqlRawAsync("delete from league.memberships where \"SeasonId\" in (select \"Id\" from league.seasons where \"State\" = 0); delete from league.seasons where \"State\" = 0");
        return 0;
    });

    [Fact]
    public async Task AnonymousAndForgedCallersAreUnauthorizedAndNothingIsCreated()
    {
        await ClearSetup();
        var before = await Footprint();
        var client = Client(fixture.Development);

        foreach (var cookie in new string?[] { null, "", SeededCoach.Value.ToString(), "b548d5d9-272d-4f4d-b2b4-f179ad8b289d", new string('A', 64) })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Post(Url, cookie), TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(before, await Footprint());
    }

    [Fact]
    public async Task APlainCoachAnInactiveCommissionerAndADeactivatedAdministratorAreRefusedWithoutAnyTrace()
    {
        await ClearSetup();
        var coach = await SessionFor(SeededCoach);
        var formerCommissioner = await SessionFor(await UserAsync("former.commissioner@example.com", UserRole.Coach, commissioner: true, commissionerActive: false));
        var deactivated = await UserAsync("deactivated.admin@example.com", UserRole.Admin);
        var deactivatedSession = await SessionFor(deactivated);
        await fixture.WithDatabase(fixture.Development, async (database, _) => await database.Database.ExecuteSqlAsync($"""update identity.users set "IsActive" = false where "Id" = {deactivated.Value}"""));
        var before = await Footprint();
        var client = Client(fixture.Development);

        var plain = await client.SendAsync(Post(Url, coach), TestContext.Current.CancellationToken);
        var former = await client.SendAsync(Post(Url, formerCommissioner), TestContext.Current.CancellationToken);
        var gone = await client.SendAsync(Post(Url, deactivatedSession), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, plain.StatusCode);
        Assert.Equal("season.creator-forbidden", (await plain.Content.ReadFromJsonAsync<Error>(cancellationToken: TestContext.Current.CancellationToken))!.Code);
        Assert.Equal(HttpStatusCode.Forbidden, former.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
        Assert.Equal(before, await Footprint());
    }

    [Fact]
    public async Task AGlobalAdministratorWithNoMembershipMayCreateASeasonAndBecomesItsCommissioner()
    {
        await ClearSetup();
        var admin = await UserAsync("lone.admin@example.com", UserRole.Admin);
        var session = await SessionFor(admin);
        var before = await Footprint();

        var response = await Client(fixture.Development).SendAsync(Post(Url, session), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await Footprint();
        Assert.Equal((before.Setup + 1, before.Memberships + 1, before.Audits + 1), after);
        var membership = await fixture.WithDatabase(fixture.Development, (database, _) => database.Memberships.AsNoTracking().SingleAsync(item => item.UserId == admin && item.Role == MembershipRole.Commissioner && item.SeasonId != new SeasonId(Guid.Parse("3908b3ef-4b67-4cf8-890f-4d72a295c8aa"))));
        Assert.True(membership.IsActive);
        await ClearSetup();
    }

    [Fact]
    public async Task OnlyOneSetupSeasonStillExistsWhoeverCreatesIt()
    {
        await ClearSetup();
        var first = await SessionFor(await UserAsync("admin.one@example.com", UserRole.Admin));
        var second = await SessionFor(await UserAsync("commissioner.two@example.com", UserRole.Coach, commissioner: true));
        var client = Client(fixture.Development);

        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Url, first), TestContext.Current.CancellationToken)).StatusCode);
        var again = await client.SendAsync(Post(Url, second, new { year = 2032, name = "2032 Swan League" }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("season.setup.exists", (await again.Content.ReadFromJsonAsync<Error>(cancellationToken: TestContext.Current.CancellationToken))!.Code);
        Assert.Equal(1, (await Footprint()).Setup);
        await ClearSetup();
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Cookie", $"swan_session={cookie}");
        return request;
    }

    // The 18-week schedule the real provider import would give a Setup season (it needs a key and the network), written straight to the database.
    private Task GiveTheSetupSeasonAScheduleAsync() => fixture.WithDatabase(fixture.Development, async (database, _) =>
    {
        await database.Database.ExecuteSqlRawAsync("""
            insert into schedule.weeks ("Id", "SeasonId", "Number", "Type", "State")
            select gen_random_uuid(), s."Id", n, '0||True', 0 from league.seasons s, generate_series(1, 18) n where s."State" = 0;
            insert into schedule.games ("Id", "HomeTeamId", "AwayTeamId", "Kickoff", "Excluded", "WeekId", "LineHistory", "ProviderGameId")
            select gen_random_uuid(), (select "Id" from schedule.teams order by "Name" limit 1), (select "Id" from schedule.teams order by "Name" offset 1 limit 1),
                   make_timestamptz(s."Year", 9, 9, 17, 0, 0, 'UTC') + (w."Number" - 1) * interval '7 days', false, w."Id", ARRAY[]::numeric[], 'creator-test-' || s."Year" || '-' || w."Number"
            from schedule.weeks w join league.seasons s on s."Id" = w."SeasonId" where s."State" = 0
            """);
        return 0;
    });

    [Fact]
    public async Task ACommissionerWhoIsOnlyACoachGloballyCanCreateReadEditFinishAndStartTheirOwnSeason()
    {
        await ClearSetup();
        var session = await SessionFor(await UserAsync("owning.commissioner@example.com", UserRole.Coach, commissioner: true));
        var client = Client(fixture.Development);
        async Task<HttpResponseMessage> Do(HttpMethod method, string url, object? body = null) => await client.SendAsync(Send(method, url, session, body));

        Assert.Equal(HttpStatusCode.Created, (await Do(HttpMethod.Post, Url, new { year = 2031, name = "2031 Swan League" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Get, $"{Url}/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Put, $"{Url}/setup", new { year = 2031, name = "2031 Swan League (renamed)" })).StatusCode);
        var catalog = (await (await Do(HttpMethod.Get, $"{Url}/setup/teams")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("teams").EnumerateArray().ToList();
        var seen = new Dictionary<string, int>();
        var finishes = catalog.Select(team =>
        {
            var division = team.GetProperty("division").GetString()!;
            seen[division] = seen.GetValueOrDefault(division) + 1;
            return new { teamId = team.GetProperty("teamId").GetGuid(), finishPosition = seen[division] };
        }).ToList();
        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Put, $"{Url}/setup/teams", new { teams = finishes })).StatusCode);
        await GiveTheSetupSeasonAScheduleAsync();
        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Put, $"{Url}/setup/double-dog-weeks", new { weeks = new[] { 2, 12, 17 } })).StatusCode);
        var payouts = new[] { "Main", "Dog", "Regular Season", "Playoff" }.Select(pool => new { pool, places = new[] { new { position = 1, percent = 100 } } }).ToList();
        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Put, $"{Url}/setup/fines-and-payouts", new { missedPickFine = 5, payouts })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Get, $"{Url}/setup/review")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Do(HttpMethod.Post, $"{Url}/setup/review/finish")).StatusCode);
        var start = await Do(HttpMethod.Post, $"{Url}/setup/activate", new { rosterDraftDeadlineEastern = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd'T'18:00") });

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var season = await fixture.WithDatabase(fixture.Development, (database, _) => database.Seasons.AsNoTracking().SingleAsync(item => item.Name == "2031 Swan League (renamed)"));
        Assert.Equal(SeasonState.Live, season.State);
        Assert.NotNull(season.RosterDraftDeadline);
        // Once started there is no Setup season left for anyone but an administrator to ask about.
        Assert.Equal(HttpStatusCode.Forbidden, (await Do(HttpMethod.Get, $"{Url}/setup")).StatusCode);
        await RemoveSeasonsNewerThanTheSeededOne();
    }

    [Fact]
    public async Task AnUnrelatedCommissionerACoachAndAnAnonymousCallerCannotTouchSomeoneElsesSetupSeason()
    {
        await ClearSetup();
        var owner = await SessionFor(await UserAsync("season.owner@example.com", UserRole.Admin));
        var client = Client(fixture.Development);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Url, owner), TestContext.Current.CancellationToken)).StatusCode);
        var unrelated = await SessionFor(await UserAsync("other.commissioner@example.com", UserRole.Coach, commissioner: true));
        var coach = await SessionFor(SeededCoach);
        var before = await Footprint();
        var changes = await fixture.WithDatabase(fixture.Development, (database, _) => database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Setup).Select(season => season.Name).SingleAsync());

        foreach (var cookie in new[] { unrelated, coach })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Get, $"{Url}/setup", cookie), TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Put, $"{Url}/setup", cookie, new { year = 2031, name = "Hijacked" }), TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Post, $"{Url}/setup/review/finish", cookie), TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Post, $"{Url}/setup/activate", cookie, new { rosterDraftDeadlineEastern = "2031-10-20T18:00" }), TestContext.Current.CancellationToken)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Url}/setup"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(before, await Footprint());
        Assert.Equal(changes, await fixture.WithDatabase(fixture.Development, (database, _) => database.Seasons.AsNoTracking().Where(season => season.State == SeasonState.Setup).Select(season => season.Name).SingleAsync()));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Send(HttpMethod.Get, $"{Url}/setup", owner), TestContext.Current.CancellationToken)).StatusCode);
        await ClearSetup();
    }

    [Fact]
    public async Task ACommissionerWhoLosesTheMembershipLosesTheSetupSeasonImmediately()
    {
        await ClearSetup();
        var commissioner = await UserAsync("short.lived.commissioner@example.com", UserRole.Coach, commissioner: true);
        var session = await SessionFor(commissioner);
        var client = Client(fixture.Development);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Url, session), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Send(HttpMethod.Get, $"{Url}/setup", session), TestContext.Current.CancellationToken)).StatusCode);

        await fixture.WithDatabase(fixture.Development, async (database, _) => await database.Database.ExecuteSqlAsync($"""update league.memberships set "IsActive" = false where "UserId" = {commissioner.Value}"""));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Get, $"{Url}/setup", session), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Post, $"{Url}/setup/review/finish", session), TestContext.Current.CancellationToken)).StatusCode);
        await ClearSetup();
    }

    [Fact]
    public async Task TheExpansionStopsAtTheSetupRoutesAndAnAdministratorKeepsEverything()
    {
        await ClearSetup();
        var commissioner = await SessionFor(await UserAsync("narrow.commissioner@example.com", UserRole.Coach, commissioner: true));
        var admin = await SessionFor(await UserAsync("still.admin@example.com", UserRole.Admin));
        var client = Client(fixture.Development);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Url, commissioner), TestContext.Current.CancellationToken)).StatusCode);

        foreach (var url in new[] { "/api/admin/dashboard", "/api/admin/audit", "/api/admin/ping" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Send(HttpMethod.Get, url, commissioner), TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Send(HttpMethod.Get, $"{Url}/setup", admin), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Send(HttpMethod.Get, "/api/admin/dashboard", admin), TestContext.Current.CancellationToken)).StatusCode);
        await ClearSetup();
    }

    private async Task<System.Text.Json.JsonElement> MeAsync(string session) =>
        await (await Client(fixture.Development).SendAsync(Get("/api/me", session))).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

    [Fact]
    public async Task MeTellsTheAppWhatThisPersonMayDoWithSetupAndRostersFromTheDatabase()
    {
        await ClearSetup();
        var admin = await SessionFor(await UserAsync("me.admin@example.com", UserRole.Admin));
        var coach = await SessionFor(SeededCoach);
        var commissionerUser = await UserAsync("me.commissioner@example.com", UserRole.Coach, commissioner: true);
        var commissioner = await SessionFor(commissionerUser);
        var client = Client(fixture.Development);

        // No Setup season: an administrator manages, a commissioner may create, a coach has nothing.
        Assert.Equal("manage", (await MeAsync(admin)).GetProperty("setupAccess").GetString());
        Assert.Equal("create", (await MeAsync(commissioner)).GetProperty("setupAccess").GetString());
        Assert.Equal("none", (await MeAsync(coach)).GetProperty("setupAccess").GetString());

        // The commissioner creates one and now manages it; another commissioner can neither create nor manage.
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Url, commissioner), TestContext.Current.CancellationToken)).StatusCode);
        var other = await SessionFor(await UserAsync("me.other.commissioner@example.com", UserRole.Coach, commissioner: true));
        Assert.Equal("manage", (await MeAsync(commissioner)).GetProperty("setupAccess").GetString());
        Assert.Equal("none", (await MeAsync(other)).GetProperty("setupAccess").GetString());
        Assert.Equal("none", (await MeAsync(coach)).GetProperty("setupAccess").GetString());
        Assert.Equal("manage", (await MeAsync(admin)).GetProperty("setupAccess").GetString());

        // Losing the membership takes it away at once.
        await fixture.WithDatabase(fixture.Development, async (database, _) => await database.Database.ExecuteSqlAsync($"""update league.memberships set "IsActive" = false where "UserId" = {commissionerUser.Value}"""));
        Assert.Equal("none", (await MeAsync(commissioner)).GetProperty("setupAccess").GetString());
        await ClearSetup();
    }

    [Fact]
    public async Task MeSaysWhoCommissionsTheLiveSeasonSoTheirRosterViewIsReachable()
    {
        await RemoveSeasonsNewerThanTheSeededOne();
        var seededAdmin = await SessionFor(new UserId(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d")));
        var coach = await SessionFor(SeededCoach);
        var commissionerUser = await UserAsync("live.commissioner@example.com", UserRole.Coach, commissioner: true);
        var commissionerCoach = await SessionFor(commissionerUser);
        var plainAdmin = await SessionFor(await UserAsync("live.plain.admin@example.com", UserRole.Admin));

        await fixture.WithDatabase(fixture.Development, async (database, _) =>
        {
            var live = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2032, "Live access test season", true).Value!;
            Assert.True(live.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
            live.CompleteSetup();
            Assert.True(live.Start(hasActiveCommissioner: true).IsSuccess);
            database.Seasons.Add(live);
            database.Memberships.AddRange(
                new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d")), live.Id, MembershipRole.Commissioner),
                new Membership(new MembershipId(Guid.NewGuid()), commissionerUser, live.Id, MembershipRole.Commissioner));
            await database.SaveChangesAsync();
            return 0;
        });

        Assert.True((await MeAsync(seededAdmin)).GetProperty("commissionsLiveSeason").GetBoolean());
        Assert.True((await MeAsync(commissionerCoach)).GetProperty("commissionsLiveSeason").GetBoolean());
        Assert.False((await MeAsync(coach)).GetProperty("commissionsLiveSeason").GetBoolean());
        Assert.False((await MeAsync(plainAdmin)).GetProperty("commissionsLiveSeason").GetBoolean());
        await RemoveSeasonsNewerThanTheSeededOne();
    }
}
