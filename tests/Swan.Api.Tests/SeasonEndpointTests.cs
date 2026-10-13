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
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Services.League;
using Xunit;

namespace Swan.Api.Tests;

public sealed class SeasonEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("POST", "/api/admin/seasons")]
    [InlineData("GET", "/api/admin/seasons/setup")]
    [InlineData("PUT", "/api/admin/seasons/setup")]
    [InlineData("PUT", "/api/admin/seasons/setup/money")]
    [InlineData("PUT", "/api/admin/seasons/setup/deadline")]
    [InlineData("PUT", "/api/admin/seasons/setup/step")]
    [InlineData("GET", "/api/admin/seasons/setup/teams")]
    [InlineData("PUT", "/api/admin/seasons/setup/teams")]
    public async Task CoachIsForbiddenFromSeasonEndpoints(string method, string url)
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Coach", service);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method != "GET" ? JsonContent.Create(new { year = 2026, name = "x" }) : null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task AdminCreatesAndReadsTheSetupSeason()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var created = await client.PostAsJsonAsync("/api/admin/seasons", new { year = 2026, name = "2026 Season", isSimulation = true, buyIn = 1 });
        var read = await client.GetAsync("/api/admin/seasons/setup");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        read.EnsureSuccessStatusCode();
        Assert.Equal(["create:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:2026:2026 Season:True", "get"], service.Calls);
    }

    [Theory]
    [InlineData("season.name", HttpStatusCode.BadRequest)]
    [InlineData("season.year", HttpStatusCode.BadRequest)]
    [InlineData("season.setup.exists", HttpStatusCode.Conflict)]
    public async Task RejectionsMapToExpectedStatusCodes(string code, HttpStatusCode expected)
    {
        using var client = CreateClient("Admin", new FakeSeasonService { Failure = new Error(code, "rejected") });

        var response = await client.PostAsJsonAsync("/api/admin/seasons", new { year = 2026, name = "x" });

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingNameIsTreatedAsBlankAndNoSetupSeasonIs404()
    {
        var service = new FakeSeasonService { Failure = new Error("season.missing", "No season is in Setup.") };
        using var client = CreateClient("Admin", service);

        var read = await client.GetAsync("/api/admin/seasons/setup");
        var create = await client.PostAsJsonAsync("/api/admin/seasons", new { year = 2026 });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Contains("create:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:2026::False", service.Calls);
        Assert.NotEqual(HttpStatusCode.InternalServerError, create.StatusCode);
    }

    [Fact]
    public async Task AdminEditsTheSetupSeasonBasics()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup", new { year = 2027, name = "2027 Season", isSimulation = true, buyIn = 1 });

        response.EnsureSuccessStatusCode();
        Assert.Equal(["update:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:2027:2027 Season:True"], service.Calls);
    }

    [Fact]
    public async Task AnonymousCannotEditTheSetupSeason()
    {
        var service = new FakeSeasonService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ISeasonSetupService>(service))).CreateClient();

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup", new { year = 2027, name = "x" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("season.name", HttpStatusCode.BadRequest)]
    [InlineData("season.year", HttpStatusCode.BadRequest)]
    [InlineData("season.missing", HttpStatusCode.NotFound)]
    [InlineData("season.locked", HttpStatusCode.Conflict)]
    public async Task EditRejectionsMapToExpectedStatusCodes(string code, HttpStatusCode expected)
    {
        using var client = CreateClient("Admin", new FakeSeasonService { Failure = new Error(code, "rejected") });

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup", new { year = 2027, name = "x" });

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminEditsTheSetupSeasonMoney()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/money", new { buyIn = 150, main = 90, dog = 30, regularSeason = 15, playoff = 15, currency = "EUR" });

        response.EnsureSuccessStatusCode();
        Assert.Equal(["money:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:150/90/30/15/15:USD"], service.Calls);
    }

    [Fact]
    public async Task AnonymousCannotEditTheSetupSeasonMoney()
    {
        var service = new FakeSeasonService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ISeasonSetupService>(service))).CreateClient();

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/money", new { buyIn = 100, main = 60, dog = 20, regularSeason = 10, playoff = 10 });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("season.money", HttpStatusCode.BadRequest)]
    [InlineData("season.missing", HttpStatusCode.NotFound)]
    [InlineData("season.locked", HttpStatusCode.Conflict)]
    public async Task MoneyRejectionsMapToExpectedStatusCodes(string code, HttpStatusCode expected)
    {
        using var client = CreateClient("Admin", new FakeSeasonService { Failure = new Error(code, "rejected") });

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/money", new { buyIn = 100, main = 60, dog = 20, regularSeason = 10, playoff = 10 });

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminEditsTheSetupSeasonDeadline()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/deadline", new { teamPickDeadlineEastern = "11:30:00", ignored = 1 });

        response.EnsureSuccessStatusCode();
        Assert.Equal(["deadline:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:11:30:00"], service.Calls);
    }

    [Fact]
    public async Task AnonymousCannotEditTheSetupSeasonDeadline()
    {
        var service = new FakeSeasonService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ISeasonSetupService>(service))).CreateClient();

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/deadline", new { teamPickDeadlineEastern = "11:30:00" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"teamPickDeadlineEastern":null}""")]
    [InlineData("""{"teamPickDeadlineEastern":"not a time"}""")]
    [InlineData("""{"teamPickDeadlineEastern":"25:00:00"}""")]
    [InlineData("""{"teamPickDeadlineEastern":1130}""")]
    public async Task MissingNullOrMalformedDeadlineIsBadRequestAndNeverReachesTheService(string body)
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsync("/api/admin/seasons/setup/deadline", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("season.missing", HttpStatusCode.NotFound)]
    [InlineData("season.locked", HttpStatusCode.Conflict)]
    public async Task DeadlineRejectionsMapToExpectedStatusCodes(string code, HttpStatusCode expected)
    {
        using var client = CreateClient("Admin", new FakeSeasonService { Failure = new Error(code, "rejected") });

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/deadline", new { teamPickDeadlineEastern = "11:30:00" });

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminSavesTheSetupResumeStep()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/step", new { step = 1, ignored = true });

        response.EnsureSuccessStatusCode();
        Assert.Equal(["step:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:Money"], service.Calls);
        Assert.Contains("\"setupStep\":0", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnonymousCannotSaveTheSetupResumeStep()
    {
        var service = new FakeSeasonService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ISeasonSetupService>(service))).CreateClient();

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/step", new { step = 1 });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"step":null}""")]
    [InlineData("""{"step":"later"}""")]
    [InlineData("""{"step":1.5}""")]
    public async Task MissingNullOrMalformedStepIsBadRequestAndNeverReachesTheService(string body)
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsync("/api/admin/seasons/setup/step", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("season.step", HttpStatusCode.BadRequest)]
    [InlineData("season.missing", HttpStatusCode.NotFound)]
    [InlineData("season.locked", HttpStatusCode.Conflict)]
    public async Task StepRejectionsMapToExpectedStatusCodes(string code, HttpStatusCode expected)
    {
        using var client = CreateClient("Admin", new FakeSeasonService { Failure = new Error(code, "rejected") });

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/step", new { step = 1 });

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    private static readonly string TeamA = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task AdminReadsTheSetupTeams()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.GetAsync("/api/admin/seasons/setup/teams");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"division\":\"AFC East\"", body);
        Assert.Contains("\"teamId\":\"11111111-1111-1111-1111-111111111111\"", body);
        Assert.Equal(["teams:get"], service.Calls);
    }

    [Fact]
    public async Task AdminSavesTheSetupTeamFinishes()
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsJsonAsync("/api/admin/seasons/setup/teams", new { teams = new[] { new { teamId = TeamA, finishPosition = 2, pointValue = 99 } } });

        response.EnsureSuccessStatusCode();
        Assert.Equal([$"teams:put:9c9b2c10-a312-4b77-a26a-ef546e87f3c9:{TeamA}=2"], service.Calls);
        Assert.Contains("\"pointValue\":2", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnonymousCannotReadOrSaveTheSetupTeams()
    {
        var service = new FakeSeasonService();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ISeasonSetupService>(service))).CreateClient();

        var read = await client.GetAsync("/api/admin/seasons/setup/teams");
        var write = await client.PutAsJsonAsync("/api/admin/seasons/setup/teams", new { teams = Array.Empty<object>() });

        Assert.All(new[] { read.StatusCode, write.StatusCode }, status => Assert.Contains(status, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect }));
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"teams":null}""")]
    [InlineData("""{"teams":[null]}""")]
    [InlineData("""{"teams":"all"}""")]
    [InlineData("""{"teams":[{"teamId":"not-a-guid","finishPosition":1}]}""")]
    [InlineData("""{"teams":[{"teamId":"11111111-1111-1111-1111-111111111111","finishPosition":"first"}]}""")]
    public async Task MissingNullOrMalformedTeamsAreBadRequestAndNeverReachTheService(string body)
    {
        var service = new FakeSeasonService();
        using var client = CreateClient("Admin", service);

        var response = await client.PutAsync("/api/admin/seasons/setup/teams", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("season.teams", HttpStatusCode.BadRequest)]
    [InlineData("season.missing", HttpStatusCode.NotFound)]
    [InlineData("season.locked", HttpStatusCode.Conflict)]
    public async Task TeamRejectionsMapToExpectedStatusCodes(string code, HttpStatusCode expected)
    {
        using var client = CreateClient("Admin", new FakeSeasonService { Failure = new Error(code, "rejected") });

        var read = await client.GetAsync("/api/admin/seasons/setup/teams");
        var write = await client.PutAsJsonAsync("/api/admin/seasons/setup/teams", new { teams = new[] { new { teamId = TeamA, finishPosition = 1 } } });

        Assert.Equal(expected, write.StatusCode);
        Assert.Contains(code, await write.Content.ReadAsStringAsync());
        if (code == "season.missing") Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    private HttpClient CreateClient(string role, FakeSeasonService service) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton<ISeasonSetupService>(service);
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

    private sealed class FakeSeasonService : ISeasonSetupService
    {
        public List<string> Calls { get; } = [];
        public Error? Failure { get; init; }

        public Task<Result<SeasonSummary>> CreateBlankAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default)
        {
            Calls.Add($"create:{actor.Value}:{year}:{name}:{isSimulation}");
            return Task.FromResult(Failure is { } error && error.Code != "season.missing" ? Result<SeasonSummary>.Failure(error.Code, error.Message) : Ok());
        }

        public Task<Result<SeasonSummary>> GetSetupAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("get");
            return Task.FromResult(Failure is { } error ? Result<SeasonSummary>.Failure(error.Code, error.Message) : Ok());
        }

        public Task<Result<SeasonSummary>> UpdateSetupBasicsAsync(UserId actor, int year, string name, bool isSimulation, CancellationToken cancellationToken = default)
        {
            Calls.Add($"update:{actor.Value}:{year}:{name}:{isSimulation}");
            return Task.FromResult(Failure is { } error ? Result<SeasonSummary>.Failure(error.Code, error.Message) : Ok());
        }

        public Task<Result<SeasonSummary>> UpdateSetupMoneyAsync(UserId actor, SetupMoney money, CancellationToken cancellationToken = default)
        {
            Calls.Add($"money:{actor.Value}:{money.BuyIn.Amount}/{money.Main.Amount}/{money.Dog.Amount}/{money.RegularSeason.Amount}/{money.Playoff.Amount}:{money.BuyIn.Currency}");
            return Task.FromResult(Failure is { } error ? Result<SeasonSummary>.Failure(error.Code, error.Message) : Ok());
        }

        public Task<Result<SeasonSummary>> UpdateSetupDeadlineAsync(UserId actor, TimeOnly deadlineEastern, CancellationToken cancellationToken = default)
        {
            Calls.Add($"deadline:{actor.Value}:{deadlineEastern:HH:mm:ss}");
            return Task.FromResult(Failure is { } error ? Result<SeasonSummary>.Failure(error.Code, error.Message) : Ok());
        }

        public Task<Result<SeasonSummary>> UpdateSetupStepAsync(UserId actor, SetupStep step, CancellationToken cancellationToken = default)
        {
            Calls.Add($"step:{actor.Value}:{step}");
            return Task.FromResult(Failure is { } error ? Result<SeasonSummary>.Failure(error.Code, error.Message) : Ok());
        }

        private static Result<SetupTeams> OkTeams(params SetupTeam[] teams) => Result<SetupTeams>.Success(new SetupTeams(teams.Length > 0 ? teams : [new SetupTeam(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Bills", "AFC East", null, null)]));

        public Task<Result<SetupTeams>> GetSetupTeamsAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("teams:get");
            return Task.FromResult(Failure is { } error ? Result<SetupTeams>.Failure(error.Code, error.Message) : OkTeams());
        }

        public Task<Result<SetupTeams>> UpdateSetupTeamsAsync(UserId actor, IReadOnlyList<TeamFinish> finishes, CancellationToken cancellationToken = default)
        {
            Calls.Add($"teams:put:{actor.Value}:{string.Join(",", finishes.Select(entry => $"{entry.TeamId.Value}={entry.Finish}"))}");
            return Task.FromResult(Failure is { } error ? Result<SetupTeams>.Failure(error.Code, error.Message) : OkTeams(finishes.Select(entry => new SetupTeam(entry.TeamId.Value, "Bills", "AFC East", entry.Finish, entry.Finish)).ToArray()));
        }

        private static Result<SeasonSummary> Ok() => Result<SeasonSummary>.Success(new SeasonSummary(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", false, SeasonState.Setup, new SeasonSettings(new Money(100m, "USD"), 3), []));
    }
}
