using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swan.Core.Common;
using Swan.Services.Scoring;
using Xunit;

namespace Swan.Api.Tests;

public sealed class CoachStandingsEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/coach/standings";

    [Fact]
    public async Task AnonymousIsUnauthorized()
    {
        using var client = factory.CreateClient();

        Assert.True((await client.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task AdministratorIsForbidden()
    {
        using var client = CreateClient("Admin", new FakeService());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CoachReadsTheRequestedPoolAndTheirIdentityComesFromTheSession()
    {
        var service = new FakeService();
        using var client = CreateClient("Coach", service);

        var response = await client.GetAsync($"{Url}?pool=Dog", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CoachStandings>(TestContext.Current.CancellationToken);
        Assert.Equal(("ready", "Dog", 7, 2), (body!.State, body.Pool, body.ThroughWeek, body.Rows.Count));
        Assert.Equal(3, body.You!.BehindLeader);
        Assert.Equal(["get:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:Dog"], service.Calls);
    }

    [Fact]
    public async Task UnknownPoolIsBadRequest()
    {
        using var client = CreateClient("Coach", new FakeService());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}?pool=Wrong", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("no-membership")]
    [InlineData("no-scores")]
    public async Task CoachReceivesTheServiceStateWhenStandingsAreNotReady(string state)
    {
        using var client = CreateClient("Coach", new FakeService(new CoachStandings(state, null, "Main", null, 0, [], null)));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(state, (await response.Content.ReadFromJsonAsync<CoachStandings>(TestContext.Current.CancellationToken))!.State);
    }

    private HttpClient CreateClient(string role, ICoachStandingsService service) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options => options.DefaultAuthenticateScheme = "Test")
            .AddScheme<AuthenticationSchemeOptions, TestHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton(service);
    })).CreateClient();

    private sealed class TestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "9c9b2c10-a312-4b77-a26a-ef546e87f3c9"), new Claim(ClaimTypes.Role, Options.ClaimsIssuer!)], Scheme.Name)), Scheme.Name)));
    }

    private sealed class FakeService(CoachStandings? response = null) : ICoachStandingsService
    {
        public List<string> Calls { get; } = [];
        public Task<CoachStandings> GetAsync(UserId coach, StandingPool pool, CancellationToken cancellationToken = default)
        {
            Calls.Add($"get:{coach.Value}:{pool}");
            return Task.FromResult(response ?? new CoachStandings("ready", "2026 Swan League", pool.ToString(), 7, 2,
                [new CoachStanding(Guid.NewGuid(), 1, "Coach 1", 11, 4, false, 1), new CoachStanding(Guid.Parse("9c9b2c10-a312-4b77-a26a-ef546e87f3c9"), 2, "Coach 2", 8, 3, true, -1)],
                new CoachStandingWithLeader(Guid.Parse("9c9b2c10-a312-4b77-a26a-ef546e87f3c9"), 2, "Coach 2", 8, 3, true, -1, 3)));
        }
    }
}
