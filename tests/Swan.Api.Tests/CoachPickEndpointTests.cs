using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swan.Core.Common;
using Swan.Services.Picks;
using Xunit;

namespace Swan.Api.Tests;

public sealed class CoachPickEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/coach/picks/current";
    private static readonly Guid WeekGuid = Guid.NewGuid();
    private static readonly Guid TeamGuid = Guid.NewGuid();

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task AnonymousIsNotLetIn(string method)
    {
        var service = new FakePickService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ICoachPickService>(service))).CreateClient();

        var response = await client.SendAsync(Request(method), TestContext.Current.CancellationToken);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task AnAdministratorIsForbiddenBecauseTheDeskIsForCoaches(string method)
    {
        var service = new FakePickService();
        using var client = CreateClient("Admin", service);

        var response = await client.SendAsync(Request(method), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task ACoachReadsTheirDeskAsReadableJsonAndTheActorComesFromTheSession()
    {
        var service = new FakePickService();
        using var client = CreateClient("Coach", service);

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal(("open", "2026 Swan League", 3), (root.GetProperty("state").GetString(), root.GetProperty("seasonName").GetString(), root.GetProperty("revision").GetInt32()));
        Assert.Equal(WeekGuid, root.GetProperty("week").GetProperty("id").GetGuid());
        Assert.Equal("Team 00", root.GetProperty("games")[0].GetProperty("team").GetString());
        Assert.Equal(TeamGuid, root.GetProperty("games")[0].GetProperty("teamId").GetGuid());
        Assert.Equal(2.5m, root.GetProperty("dogs")[0].GetProperty("spread").GetDecimal());
        Assert.Equal(["get:9c9b2c10-a312-4b77-a26a-ef546e87f3c9"], service.Calls);
    }

    [Fact]
    public async Task ASaveSendsTheCoachsChoicesAndReturnsTheSavedDesk()
    {
        var service = new FakePickService();
        using var client = CreateClient("Coach", service);

        var response = await client.PutAsJsonAsync(Url, new { weekId = WeekGuid, expectedRevision = 3, teamIds = new[] { TeamGuid }, dogTeamIds = new Guid[0] }, cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal([$"save:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:{WeekGuid}:3:{TeamGuid}:"], service.Calls);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("open", json.RootElement.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("pick.invalid", HttpStatusCode.BadRequest)]
    [InlineData("dog.invalid", HttpStatusCode.BadRequest)]
    [InlineData("pick.locked", HttpStatusCode.Conflict)]
    [InlineData("dog.locked", HttpStatusCode.Conflict)]
    [InlineData("ballot.stale", HttpStatusCode.Conflict)]
    [InlineData("week.stale", HttpStatusCode.Conflict)]
    [InlineData("pick.unavailable", HttpStatusCode.Conflict)]
    public async Task EachRefusalHasAStableCodeAndStatus(string code, HttpStatusCode status)
    {
        using var client = CreateClient("Coach", new FakePickService { Failure = new Error(code, "Say what to fix.") });

        var response = await client.PutAsJsonAsync(Url, new { weekId = WeekGuid, expectedRevision = 0, teamIds = new[] { TeamGuid }, dogTeamIds = new Guid[0] }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<Error>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((code, "Say what to fix."), (error!.Code, error.Message));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"weekId\":null,\"expectedRevision\":0,\"teamIds\":[],\"dogTeamIds\":[]}")]
    [InlineData("{\"expectedRevision\":0,\"teamIds\":null,\"dogTeamIds\":[]}")]
    [InlineData("{\"expectedRevision\":0,\"teamIds\":[],\"dogTeamIds\":null}")]
    [InlineData("{\"expectedRevision\":-1,\"teamIds\":[],\"dogTeamIds\":[]}")]
    [InlineData("not json")]
    public async Task AnIncompleteOrMalformedBodyIsABadRequestAndNothingIsSaved(string body)
    {
        var service = new FakePickService();
        using var client = CreateClient("Coach", service);

        var response = await client.PutAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    private static HttpRequestMessage Request(string method) => new(new HttpMethod(method), Url)
    {
        Content = method == "GET" ? null : JsonContent.Create(new { weekId = WeekGuid, expectedRevision = 0, teamIds = new Guid[0], dogTeamIds = new Guid[0] })
    };

    private HttpClient CreateClient(string role, FakePickService service) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton<ICoachPickService>(service);
    })).CreateClient();

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9c9b2c10-a312-4b77-a26a-ef546e87f3c9"),
                new Claim(ClaimTypes.Role, Options.ClaimsIssuer!)
            ], Scheme.Name)), Scheme.Name)));
    }

    private sealed class FakePickService : ICoachPickService
    {
        public List<string> Calls { get; } = [];
        public Error? Failure { get; init; }

        private static PickDesk Desk() => new("open", "2026 Swan League", new DeskWeek(WeekGuid, 4, DateTimeOffset.UtcNow.AddDays(2), false, 3, 1), 3, DateTimeOffset.UtcNow,
            [new DeskGame(TeamGuid, "Team 00", Guid.NewGuid(), "Team 09", true, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1), false, 2, false)], ["Team 05"],
            [new DeskDog(Guid.NewGuid(), "Team 01", Guid.NewGuid(), "Team 10", true, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2), false, 2.5m, false)]);

        public Task<Result<PickDesk>> GetAsync(UserId coach, CancellationToken cancellationToken = default)
        {
            Calls.Add($"get:{coach.Value}");
            return Task.FromResult(Result<PickDesk>.Success(Desk()));
        }

        public Task<Result<PickDesk>> SaveAsync(UserId coach, SavePicks request, CancellationToken cancellationToken = default)
        {
            Calls.Add($"save:{coach.Value}:{request.WeekId}:{request.ExpectedRevision}:{string.Join(",", request.TeamIds)}:{string.Join(",", request.DogTeamIds)}");
            return Task.FromResult(Failure is { } error ? Result<PickDesk>.Failure(error.Code, error.Message) : Result<PickDesk>.Success(Desk()));
        }
    }
}
