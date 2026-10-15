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
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Post(Url, cookie))).StatusCode);

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

        var plain = await client.SendAsync(Post(Url, coach));
        var former = await client.SendAsync(Post(Url, formerCommissioner));
        var gone = await client.SendAsync(Post(Url, deactivatedSession));

        Assert.Equal(HttpStatusCode.Forbidden, plain.StatusCode);
        Assert.Equal("season.creator-forbidden", (await plain.Content.ReadFromJsonAsync<Error>())!.Code);
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

        var response = await Client(fixture.Development).SendAsync(Post(Url, session));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await Footprint();
        Assert.Equal((before.Setup + 1, before.Memberships + 1, before.Audits + 1), after);
        var membership = await fixture.WithDatabase(fixture.Development, (database, _) => database.Memberships.AsNoTracking().SingleAsync(item => item.UserId == admin && item.Role == MembershipRole.Commissioner && item.SeasonId != new SeasonId(Guid.Parse("3908b3ef-4b67-4cf8-890f-4d72a295c8aa"))));
        Assert.True(membership.IsActive);
        await ClearSetup();
    }

    [Fact]
    public async Task AnActiveCommissionerWhoIsOnlyACoachGloballyMayCreateASeasonButNothingElseInSetup()
    {
        await ClearSetup();
        var commissioner = await UserAsync("commissioner.coach@example.com", UserRole.Coach, commissioner: true);
        var session = await SessionFor(commissioner);
        var client = Client(fixture.Development);

        var created = await client.SendAsync(Post(Url, session));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, (await Footprint()).Setup);
        // Every other season route stays Admin-only, so this commissioner cannot edit, read or activate it.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Get($"{Url}/setup", session))).StatusCode);
        var put = new HttpRequestMessage(HttpMethod.Put, $"{Url}/setup") { Content = JsonContent.Create(new { year = 2031, name = "Renamed" }) };
        put.Headers.Add("Cookie", $"swan_session={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(put)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Post($"{Url}/setup/activate", session, new { rosterDraftDeadlineEastern = "2031-10-20T18:00" }))).StatusCode);
        await ClearSetup();
    }

    [Fact]
    public async Task OnlyOneSetupSeasonStillExistsWhoeverCreatesIt()
    {
        await ClearSetup();
        var first = await SessionFor(await UserAsync("admin.one@example.com", UserRole.Admin));
        var second = await SessionFor(await UserAsync("commissioner.two@example.com", UserRole.Coach, commissioner: true));
        var client = Client(fixture.Development);

        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Url, first))).StatusCode);
        var again = await client.SendAsync(Post(Url, second, new { year = 2032, name = "2032 Swan League" }));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("season.setup.exists", (await again.Content.ReadFromJsonAsync<Error>())!.Code);
        Assert.Equal(1, (await Footprint()).Setup);
        await ClearSetup();
    }
}
