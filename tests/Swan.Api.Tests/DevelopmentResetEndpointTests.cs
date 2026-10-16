using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Identity;
using Xunit;

namespace Swan.Api.Tests;

// The route exists only in Development, and even there only for an administrator. Real handler, real routes, real database.
public sealed class DevelopmentResetEndpointTests(SessionApiFixture fixture) : IClassFixture<SessionApiFixture>
{
    private const string Url = "/api/admin/development/reset-season-data";
    private static readonly UserId SeededAdmin = new(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d"));
    private static readonly UserId SeededCoach = new(Guid.Parse("00000000-0000-0000-0000-000000000201"));

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private static HttpRequestMessage Request(HttpMethod method, string url, string? cookie)
    {
        var request = new HttpRequestMessage(method, url);
        if (cookie is not null) request.Headers.Add("Cookie", $"swan_session={cookie}");
        return request;
    }

    private Task<string> SessionFor(WebApplicationFactory<Program> factory, UserId user) => fixture.WithDatabase(factory, async (_, sessions) => (await sessions.CreateAsync(user)).Credential);

    private async Task<int> Seasons() => await fixture.WithDatabase(fixture.Development, (database, _) => database.Seasons.CountAsync());

    [Fact]
    public async Task OutsideDevelopmentTheRouteDoesNotExistForAnyoneAndNothingIsRemoved()
    {
        var admin = await SessionFor(fixture.Production, SeededAdmin);
        var before = await Seasons();
        var client = Client(fixture.Production);

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request(HttpMethod.Post, Url, admin))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request(HttpMethod.Post, Url, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request(HttpMethod.Get, Url, admin))).StatusCode);
        Assert.Equal(before, await Seasons());
        Assert.True(before > 0);
    }

    [Fact]
    public async Task InDevelopmentAnonymousCoachesAndCommissionersWhoAreNotAdministratorsAreRefusedAndNothingIsRemoved()
    {
        var coach = await SessionFor(fixture.Development, SeededCoach);
        var commissioner = Guid.NewGuid();
        await fixture.WithDatabase(fixture.Development, async (database, _) =>
        {
            database.Users.Add(new User(new UserId(commissioner), Email.Create("reset.commissioner@example.com").Value!, "Reset Commissioner", UserRole.Coach));
            await database.SaveChangesAsync();
            return 0;
        });
        var commissionerSession = await SessionFor(fixture.Development, new UserId(commissioner));
        var before = await Seasons();
        var client = Client(fixture.Development);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(HttpMethod.Post, Url, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(HttpMethod.Post, Url, SeededCoach.Value.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(HttpMethod.Post, Url, coach))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(HttpMethod.Post, Url, commissionerSession))).StatusCode);
        Assert.Equal(before, await Seasons());
    }

    [Fact]
    public async Task MeOffersDevelopmentToolsOnlyToAnAdministratorInDevelopment()
    {
        var devAdmin = await SessionFor(fixture.Development, SeededAdmin);
        var devCoach = await SessionFor(fixture.Development, SeededCoach);
        var prodAdmin = await SessionFor(fixture.Production, SeededAdmin);

        async Task<bool> Tools(WebApplicationFactory<Program> factory, string session) =>
            (await (await Client(factory).SendAsync(Request(HttpMethod.Get, "/api/me", session))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("developmentTools").GetBoolean();

        Assert.True(await Tools(fixture.Development, devAdmin));
        Assert.False(await Tools(fixture.Development, devCoach));
        Assert.False(await Tools(fixture.Production, prodAdmin));
    }

    [Fact]
    public async Task AnAdministratorInDevelopmentClearsTheSeasonDataStaysSignedInAndCanStartOver()
    {
        var admin = await SessionFor(fixture.Development, SeededAdmin);
        var client = Client(fixture.Development);
        Assert.True(await Seasons() > 0);

        var response = await client.SendAsync(Request(HttpMethod.Post, Url, admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var removed = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed");
        Assert.True(removed.GetProperty("seasons").GetInt32() > 0);
        Assert.Equal(0, await Seasons());
        // The administrator is not signed out (people and sessions are kept), there is no Setup season, and a new season can be created straight away.
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request(HttpMethod.Get, "/api/me", admin))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request(HttpMethod.Get, "/api/admin/seasons/setup", admin))).StatusCode);
        var create = Request(HttpMethod.Post, "/api/admin/seasons", admin);
        create.Content = JsonContent.Create(new { year = 2031, name = "A fresh start" });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);
        var teams = await fixture.WithDatabase(fixture.Development, (database, _) => database.Teams.CountAsync());
        Assert.Equal(32, teams);
    }
}
