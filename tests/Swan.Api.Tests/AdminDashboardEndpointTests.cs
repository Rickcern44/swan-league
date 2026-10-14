using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Services.Schedule;
using Xunit;

namespace Swan.Api.Tests;

public sealed class AdminDashboardEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/admin/dashboard";

    [Fact]
    public async Task AnonymousIsNotLetIn()
    {
        var service = new FakeDashboardService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IAdminDashboardService>(service))).CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task CoachIsForbiddenAndNothingIsRead()
    {
        var service = new FakeDashboardService();
        using var client = CreateClient("Coach", service);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task AdminGetsTheDashboardWithReadableStatesAndKinds()
    {
        var weekId = new WeekId(Guid.NewGuid());
        var service = new FakeDashboardService(new AdminDashboard(
            new DashboardSeason(new SeasonId(Guid.NewGuid()), "2026 Season", 2026, false),
            new DashboardWeek(weekId, 4, WeekKind.Regular, WeekState.Draft, 16, 16, 13, 0, false),
            [new AttentionItem("lines-missing", "Lines need approval", "3 of 16 included games need a line", weekId, 4, WeekState.Draft, 3, 16, true)],
            false));
        using var client = CreateClient("Admin", service);

        var response = await client.GetAsync(Url);

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("2026 Season", root.GetProperty("season").GetProperty("name").GetString());
        Assert.Equal("Draft", root.GetProperty("currentWeek").GetProperty("state").GetString());
        var item = root.GetProperty("attention")[0];
        Assert.Equal(("lines-missing", 4, "Draft", 3), (item.GetProperty("kind").GetString(), item.GetProperty("weekNumber").GetInt32(), item.GetProperty("weekState").GetString(), item.GetProperty("count").GetInt32()));
        Assert.Equal(weekId.Value, item.GetProperty("weekId").GetGuid());
        Assert.False(root.GetProperty("caughtUp").GetBoolean());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task NoLiveSeasonIsASuccessfulDeliberateState()
    {
        using var client = CreateClient("Admin", new FakeDashboardService());

        var response = await client.GetAsync(Url);

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("season").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("attention").EnumerateArray());
    }

    [Fact]
    public async Task OnlyReadsAreAllowed()
    {
        using var client = CreateClient("Admin", new FakeDashboardService());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(Url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync(Url, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Url)).StatusCode);
    }

    private HttpClient CreateClient(string role, FakeDashboardService service) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton<IAdminDashboardService>(service);
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

    private sealed class FakeDashboardService(AdminDashboard? dashboard = null) : IAdminDashboardService
    {
        public int Calls { get; private set; }

        public Task<Result<AdminDashboard>> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result<AdminDashboard>.Success(dashboard ?? new AdminDashboard(null, null, [], false)));
        }
    }
}
