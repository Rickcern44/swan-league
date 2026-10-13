using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swan.Core.Common;
using Swan.Core.Schedule;
using Swan.Services.Schedule;
using Xunit;

namespace Swan.Api.Tests;

public sealed class WeekAdminEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid WeekGuid = Guid.NewGuid();
    private static readonly Guid GameGuid = Guid.NewGuid();
    private static readonly Guid SeasonGuid = Guid.NewGuid();
    private static readonly Guid TeamGuid = Guid.NewGuid();

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "/games/00000000-0000-0000-0000-000000000001/lines")]
    [InlineData("POST", "/lines/approve")]
    [InlineData("POST", "/games/00000000-0000-0000-0000-000000000001/lines/approve-latest")]
    [InlineData("POST", "/publish")]
    [InlineData("POST", "/lock")]
    [InlineData("POST", "/games/00000000-0000-0000-0000-000000000001/result")]
    [InlineData("POST", "/finalize")]
    [InlineData("POST", "/recompute")]
    [InlineData("POST", "/dog-options/generate")]
    [InlineData("PUT", "/dog-options")]
    public async Task CoachIsForbiddenFromEveryWeekEndpoint(string method, string suffix)
    {
        using var client = CreateClient("Coach", new FakeWeekService());

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/admin/weeks/{WeekGuid}{suffix}") { Content = method != "GET" ? JsonContent.Create(new { spread = 1, teamIds = new Guid[0] }) : null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SeasonRecomputeIsAdminOnlyAndReportsSummaryOr404ForMissingSeason()
    {
        var service = new FakeWeekService();
        using var coach = CreateClient("Coach", service);
        using var admin = CreateClient("Admin", service);
        using var missing = CreateClient("Admin", new FakeWeekService { SeasonFailure = new Error("season.missing", "Season does not exist.") });
        var url = $"/api/admin/seasons/{SeasonGuid}/weeks/recompute";

        Assert.Equal(HttpStatusCode.Forbidden, (await coach.PostAsync(url, null)).StatusCode);
        var ok = await admin.PostAsync(url, null);
        var notFound = await missing.PostAsync(url, null);

        ok.EnsureSuccessStatusCode();
        using var payload = System.Text.Json.JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal(2, payload.RootElement.GetProperty("recomputed").GetArrayLength());
        Assert.Equal(0, payload.RootElement.GetProperty("failed").GetArrayLength());
        Assert.Equal([$"season:{SeasonGuid}"], service.Calls);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task AdminReadsDashboardAndMissingWeekIs404()
    {
        using var client = CreateClient("Admin", new FakeWeekService());

        var found = await client.GetAsync($"/api/admin/weeks/{WeekGuid}");
        var missing = await client.GetAsync($"/api/admin/weeks/{Guid.NewGuid()}");

        found.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task AdminActionsPassActorAndRouteToService()
    {
        var service = new FakeWeekService();
        using var client = CreateClient("Admin", service);

        var line = await client.PostAsJsonAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/lines", new { spread = -3.5m, approve = true });
        var latest = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/lines/approve-latest", null);
        var approve = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/lines/approve", null);
        var publish = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/publish", null);
        var @lock = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/lock", null);
        var result = await client.PostAsJsonAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/result", new { homeScore = 21, awayScore = 21 });
        var finalize = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/finalize", null);
        var recompute = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/recompute", null);
        var generate = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/dog-options/generate", null);
        var replace = await client.PutAsJsonAsync($"/api/admin/weeks/{WeekGuid}/dog-options", new { teamIds = new[] { TeamGuid } });

        Assert.All(new[] { line, latest, approve, publish, @lock, result, finalize, recompute, generate, replace }, response => response.EnsureSuccessStatusCode());
        Assert.Equal([$"line:{GameGuid}:-3.5:True", $"latest:{GameGuid}", "approve", "publish", "lock", $"result:{GameGuid}:21:21", "finalize", "recompute", "generate", $"replace:{TeamGuid}"], service.Calls);
        Assert.All(service.Actors, actor => Assert.Equal(new UserId(Guid.Parse("9c9b2c10-a312-4b77-a26a-ef546e87f3c9")), actor));
    }

    [Fact]
    public async Task StateAndMissingGameFailuresAreReturnedWithoutExceptions()
    {
        using var client = CreateClient("Admin", new FakeWeekService { Failure = new Error("week.state", "Lines must be approved.") });
        using var missingGame = CreateClient("Admin", new FakeWeekService { Failure = new Error("game.missing", "Game does not exist.") });

        var conflict = await client.PostAsync($"/api/admin/weeks/{WeekGuid}/publish", null);
        var notFound = await missingGame.PostAsJsonAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/lines", new { spread = 1 });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("week.state", await conflict.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        using var noHistory = CreateClient("Admin", new FakeWeekService { Failure = new Error("game.line", "A line is required and must not be frozen.") });
        Assert.Equal(HttpStatusCode.Conflict, (await noHistory.PostAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/lines/approve-latest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await missingGame.PostAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/lines/approve-latest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await missingGame.PostAsJsonAsync($"/api/admin/weeks/{WeekGuid}/games/{GameGuid}/result", new { homeScore = 1, awayScore = 0 })).StatusCode);
        using var notLocked = CreateClient("Admin", new FakeWeekService { Failure = new Error("week.results", "Every included game needs a result.") });
        Assert.Equal(HttpStatusCode.Conflict, (await notLocked.PostAsync($"/api/admin/weeks/{WeekGuid}/finalize", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await notLocked.PostAsync($"/api/admin/weeks/{WeekGuid}/recompute", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await notLocked.PostAsync($"/api/admin/weeks/{WeekGuid}/dog-options/generate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await notLocked.PutAsJsonAsync($"/api/admin/weeks/{WeekGuid}/dog-options", new { teamIds = new[] { TeamGuid } })).StatusCode);
        using var missingWeek = CreateClient("Admin", new FakeWeekService { Failure = new Error("week.missing", "Week does not exist.") });
        Assert.Equal(HttpStatusCode.NotFound, (await missingWeek.PostAsync($"/api/admin/weeks/{WeekGuid}/dog-options/generate", null)).StatusCode);
    }

    private HttpClient CreateClient(string role, FakeWeekService service) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton<IWeekAdministrationService>(service);
    })).CreateClient();

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9c9b2c10-a312-4b77-a26a-ef546e87f3c9"),
                new Claim(ClaimTypes.Role, Options.ClaimsIssuer!)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class FakeWeekService : IWeekAdministrationService
    {
        public List<string> Calls { get; } = [];
        public List<UserId> Actors { get; } = [];
        public Error? Failure { get; init; }
        public Error? SeasonFailure { get; init; }

        public Task<Result<WeekDashboard>> GetAsync(WeekId weekId, CancellationToken cancellationToken = default) =>
            Task.FromResult(weekId.Value == WeekGuid ? Ok(weekId) : Result<WeekDashboard>.Failure("week.missing", "Week does not exist."));

        public Task<Result<WeekDashboard>> AddLineAsync(UserId actor, WeekId weekId, GameId gameId, decimal spread, bool approve, CancellationToken cancellationToken = default) => Record(actor, weekId, $"line:{gameId.Value}:{spread}:{approve}");
        public Task<Result<WeekDashboard>> ApproveLatestLineAsync(UserId actor, WeekId weekId, GameId gameId, CancellationToken cancellationToken = default) => Record(actor, weekId, $"latest:{gameId.Value}");
        public Task<Result<WeekDashboard>> ConfirmResultAsync(UserId actor, WeekId weekId, GameId gameId, int homeScore, int awayScore, CancellationToken cancellationToken = default) => Record(actor, weekId, $"result:{gameId.Value}:{homeScore}:{awayScore}");
        public Task<Result<WeekDashboard>> FinalizeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) => Record(actor, weekId, "finalize");
        public Task<Result<WeekDashboard>> RecomputeAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) => Record(actor, weekId, "recompute");
        public Task<Result<SeasonRecomputeSummary>> RecomputeSeasonAsync(UserId actor, SeasonId seasonId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"season:{seasonId.Value}");
            Actors.Add(actor);
            return Task.FromResult(SeasonFailure is { } error
                ? Result<SeasonRecomputeSummary>.Failure(error.Code, error.Message)
                : Result<SeasonRecomputeSummary>.Success(new SeasonRecomputeSummary(seasonId, [new RecomputedWeek(new WeekId(Guid.NewGuid()), 1), new RecomputedWeek(new WeekId(Guid.NewGuid()), 2)], [])));
        }

        public Task<Result<WeekDashboard>> GenerateDogOptionsAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) => Record(actor, weekId, "generate");
        public Task<Result<WeekDashboard>> ReplaceDogOptionsAsync(UserId actor, WeekId weekId, IReadOnlyCollection<TeamId> teamIds, CancellationToken cancellationToken = default) => Record(actor, weekId, $"replace:{string.Join(",", teamIds.Select(team => team.Value))}");
        public Task<Result<WeekDashboard>> ApproveLinesAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) => Record(actor, weekId, "approve");
        public Task<Result<WeekDashboard>> PublishAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) => Record(actor, weekId, "publish");
        public Task<Result<WeekDashboard>> LockAsync(UserId actor, WeekId weekId, CancellationToken cancellationToken = default) => Record(actor, weekId, "lock");

        private Task<Result<WeekDashboard>> Record(UserId actor, WeekId weekId, string call)
        {
            Calls.Add(call);
            Actors.Add(actor);
            return Task.FromResult(Failure is { } error ? Result<WeekDashboard>.Failure(error.Code, error.Message) : Ok(weekId));
        }

        private static Result<WeekDashboard> Ok(WeekId weekId) => Result<WeekDashboard>.Success(new WeekDashboard(weekId, new SeasonId(Guid.NewGuid()), 1, WeekKind.Regular, WeekState.Draft, [], []));
    }
}
