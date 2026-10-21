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

public sealed class RosterDraftEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid SeasonGuid = Guid.NewGuid();
    private static readonly Guid MembershipGuid = Guid.NewGuid();
    private static readonly Guid TeamGuid = Guid.NewGuid();
    private const string Actor = "9c9b2c10-a312-4b77-a26a-ef546e87f3c9";

    [Theory]
    [InlineData("GET", "/api/coach/roster")]
    [InlineData("PUT", "/api/coach/roster")]
    [InlineData("POST", "/api/coach/roster/lock")]
    [InlineData("GET", "/api/commissioner/roster-draft")]
    [InlineData("POST", "/api/commissioner/roster-draft/start")]
    [InlineData("POST", "/api/commissioner/roster-draft/corrections")]
    public async Task AnonymousIsNotLetIn(string method, string url)
    {
        var coach = new FakeCoachRoster();
        var commissioner = new FakeCommissionerRoster();
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ICoachRosterService>(coach);
            services.AddSingleton<ICommissionerRosterService>(commissioner);
        })).CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { }) }, TestContext.Current.CancellationToken);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
        Assert.Empty(coach.Calls);
        Assert.Empty(commissioner.Calls);
    }

    [Theory]
    [InlineData("GET", "/api/coach/roster")]
    [InlineData("PUT", "/api/coach/roster")]
    [InlineData("POST", "/api/coach/roster/lock")]
    public async Task AnAdministratorIsForbiddenFromTheCoachRosterDesk(string method, string url)
    {
        var coach = new FakeCoachRoster();
        using var client = CreateClient("Admin", coach, new FakeCommissionerRoster());

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { seasonId = SeasonGuid, expectedRevision = 0, teamIds = new Guid[0] }) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(coach.Calls);
    }

    [Fact]
    public async Task ACoachReadsSavesAndLocksTheirOwnRosterAsTheSignedInUser()
    {
        var coach = new FakeCoachRoster();
        using var client = CreateClient("Coach", coach, new FakeCommissionerRoster());

        var read = await client.GetAsync("/api/coach/roster", TestContext.Current.CancellationToken);
        var save = await client.PutAsJsonAsync("/api/coach/roster", new { seasonId = SeasonGuid, expectedRevision = 2, teamIds = new[] { TeamGuid } }, cancellationToken: TestContext.Current.CancellationToken);
        var @lock = await client.PostAsJsonAsync("/api/coach/roster/lock", new { seasonId = SeasonGuid, expectedRevision = 3 }, cancellationToken: TestContext.Current.CancellationToken);

        foreach (var response in new[] { read, save, @lock }) response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal(("open", "2026 Swan League"), (root.GetProperty("state").GetString(), root.GetProperty("seasonName").GetString()));
        Assert.Equal("2026-10-20T18:00", root.GetProperty("deadlineEastern").GetString());
        Assert.Equal(TeamGuid, root.GetProperty("teams")[0].GetProperty("teamId").GetGuid());
        Assert.Equal([$"get:{Actor}", $"save:{Actor}:{SeasonGuid}:2:{TeamGuid}", $"lock:{Actor}:{SeasonGuid}:3"], coach.Calls);
    }

    [Theory]
    [InlineData("roster.count", HttpStatusCode.BadRequest)]
    [InlineData("roster.duplicate", HttpStatusCode.BadRequest)]
    [InlineData("roster.team", HttpStatusCode.BadRequest)]
    [InlineData("roster.locked", HttpStatusCode.Conflict)]
    [InlineData("roster.stale", HttpStatusCode.Conflict)]
    [InlineData("season.stale", HttpStatusCode.Conflict)]
    [InlineData("roster.unavailable", HttpStatusCode.Conflict)]
    public async Task EachCoachRefusalHasAStableCodeAndStatus(string code, HttpStatusCode status)
    {
        using var client = CreateClient("Coach", new FakeCoachRoster { Failure = new Error(code, "Say what to fix.") }, new FakeCommissionerRoster());

        var save = await client.PutAsJsonAsync("/api/coach/roster", new { seasonId = SeasonGuid, expectedRevision = 0, teamIds = new[] { TeamGuid } }, cancellationToken: TestContext.Current.CancellationToken);
        var @lock = await client.PostAsJsonAsync("/api/coach/roster/lock", new { seasonId = SeasonGuid, expectedRevision = 0 }, cancellationToken: TestContext.Current.CancellationToken);

        foreach (var response in new[] { save, @lock })
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal((code, "Say what to fix."), ((await response.Content.ReadFromJsonAsync<Error>(cancellationToken: TestContext.Current.CancellationToken))!.Code, "Say what to fix."));
        }
    }

    [Theory]
    [InlineData("PUT", "/api/coach/roster", "{}")]
    [InlineData("PUT", "/api/coach/roster", "{\"seasonId\":null,\"expectedRevision\":0,\"teamIds\":[]}")]
    [InlineData("PUT", "/api/coach/roster", "{\"seasonId\":\"00000000-0000-0000-0000-000000000001\",\"expectedRevision\":0,\"teamIds\":null}")]
    [InlineData("PUT", "/api/coach/roster", "{\"seasonId\":\"00000000-0000-0000-0000-000000000001\",\"expectedRevision\":-1,\"teamIds\":[]}")]
    [InlineData("POST", "/api/coach/roster/lock", "{}")]
    [InlineData("POST", "/api/coach/roster/lock", "{\"seasonId\":\"00000000-0000-0000-0000-000000000001\",\"expectedRevision\":-1}")]
    [InlineData("PUT", "/api/coach/roster", "not json")]
    public async Task AnIncompleteOrMalformedCoachBodyIsABadRequestAndNothingIsSaved(string method, string url, string body)
    {
        var coach = new FakeCoachRoster();
        using var client = CreateClient("Coach", coach, new FakeCommissionerRoster());

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = new StringContent(body, Encoding.UTF8, "application/json") }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(coach.Calls);
    }

    [Fact]
    public async Task ACoachIsForbiddenFromTheCommissionerViewBecauseTheyAreNotItsCommissioner()
    {
        var commissioner = new FakeCommissionerRoster { Failure = new Error("commissioner.required", "Only the season's commissioner can manage its rosters.") };
        using var client = CreateClient("Coach", new FakeCoachRoster(), commissioner);

        var read = await client.GetAsync("/api/commissioner/roster-draft", TestContext.Current.CancellationToken);
        var correct = await client.PostAsJsonAsync("/api/commissioner/roster-draft/corrections", new { seasonId = SeasonGuid, membershipId = MembershipGuid, expectedRevision = 0, addTeamId = TeamGuid, reason = "x" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, correct.StatusCode);
        Assert.Equal("commissioner.required", (await correct.Content.ReadFromJsonAsync<Error>(cancellationToken: TestContext.Current.CancellationToken))!.Code);
    }

    [Fact]
    public async Task TheCommissionerReadsTheDraftAndCorrectsARosterAsTheSignedInUser()
    {
        var service = new FakeCommissionerRoster();
        using var client = CreateClient("Admin", new FakeCoachRoster(), service);

        var read = await client.GetAsync("/api/commissioner/roster-draft", TestContext.Current.CancellationToken);
        var correct = await client.PostAsJsonAsync("/api/commissioner/roster-draft/corrections", new { seasonId = SeasonGuid, membershipId = MembershipGuid, expectedRevision = 4, removeTeamId = TeamGuid, addTeamId = Guid.Parse("00000000-0000-0000-0000-0000000000aa"), reason = "Picked the wrong team" }, cancellationToken: TestContext.Current.CancellationToken);

        read.EnsureSuccessStatusCode();
        correct.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var coach = json.RootElement.GetProperty("coaches")[0];
        Assert.Equal(("Coach A", "missing"), (coach.GetProperty("name").GetString(), coach.GetProperty("status").GetString()));
        Assert.Equal(MembershipGuid, coach.GetProperty("membershipId").GetGuid());
        Assert.Equal(3, json.RootElement.GetProperty("teams")[0].GetProperty("pointValue").GetInt32());
        Assert.Equal([$"get:{Actor}", $"correct:{Actor}:{MembershipGuid}:4:{TeamGuid}:00000000-0000-0000-0000-0000000000aa:Picked the wrong team"], service.Calls);
    }

    [Fact]
    public async Task TheCommissionerCanOpenAnUnopenedDraftWithAnEasternDeadline()
    {
        var service = new FakeCommissionerRoster();
        using var client = CreateClient("Admin", new FakeCoachRoster(), service);

        var response = await client.PostAsJsonAsync("/api/commissioner/roster-draft/start", new { seasonId = SeasonGuid, rosterDraftDeadlineEastern = "2026-10-20T18:00" }, cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal([$"start:{Actor}:{SeasonGuid}:2026-10-20T18:00:00"], service.Calls);
    }

    [Fact]
    public async Task TheCommissionerCanQueueDraftReminders()
    {
        var service = new FakeCommissionerRoster();
        using var client = CreateClient("Admin", new FakeCoachRoster(), service);

        var response = await client.PostAsync("/api/commissioner/roster-draft/reminders", null, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<ReminderQueueResult>(cancellationToken: TestContext.Current.CancellationToken))!.Queued);
        Assert.Equal([$"remind:{Actor}"], service.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"seasonId\":\"00000000-0000-0000-0000-000000000001\",\"rosterDraftDeadlineEastern\":\"tomorrow\"}")]
    public async Task StartingADraftNeedsASeasonAndEasternDeadline(string body)
    {
        var service = new FakeCommissionerRoster();
        using var client = CreateClient("Admin", new FakeCoachRoster(), service);

        var response = await client.PostAsync("/api/commissioner/roster-draft/start", new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("commissioner.required", HttpStatusCode.Forbidden)]
    [InlineData("season.missing", HttpStatusCode.NotFound)]
    [InlineData("coach.missing", HttpStatusCode.NotFound)]
    [InlineData("roster.reason", HttpStatusCode.BadRequest)]
    [InlineData("roster.team", HttpStatusCode.BadRequest)]
    [InlineData("roster.correction.invalid", HttpStatusCode.BadRequest)]
    [InlineData("roster.duplicate", HttpStatusCode.BadRequest)]
    [InlineData("roster.count", HttpStatusCode.BadRequest)]
    [InlineData("roster.stale", HttpStatusCode.Conflict)]
    [InlineData("season.stale", HttpStatusCode.Conflict)]
    public async Task EachCommissionerRefusalHasAStableCodeAndStatus(string code, HttpStatusCode status)
    {
        using var client = CreateClient("Admin", new FakeCoachRoster(), new FakeCommissionerRoster { Failure = new Error(code, "Say what to fix.") });

        var response = await client.PostAsJsonAsync("/api/commissioner/roster-draft/corrections", new { seasonId = SeasonGuid, membershipId = MembershipGuid, expectedRevision = 0, addTeamId = TeamGuid, reason = "Because" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<Error>(cancellationToken: TestContext.Current.CancellationToken))!.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"seasonId\":\"00000000-0000-0000-0000-000000000001\",\"membershipId\":\"00000000-0000-0000-0000-000000000002\",\"expectedRevision\":0}")]
    [InlineData("{\"seasonId\":\"00000000-0000-0000-0000-000000000001\",\"membershipId\":\"00000000-0000-0000-0000-000000000002\",\"expectedRevision\":-1,\"addTeamId\":\"00000000-0000-0000-0000-000000000003\"}")]
    [InlineData("not json")]
    public async Task AnIncompleteCorrectionIsABadRequestAndNothingChanges(string body)
    {
        var service = new FakeCommissionerRoster();
        using var client = CreateClient("Admin", new FakeCoachRoster(), service);

        var response = await client.PostAsync("/api/commissioner/roster-draft/corrections", new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    private HttpClient CreateClient(string role, FakeCoachRoster coach, FakeCommissionerRoster commissioner) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton<ICoachRosterService>(coach);
        services.AddSingleton<ICommissionerRosterService>(commissioner);
    })).CreateClient();

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Actor),
                new Claim(ClaimTypes.Role, Options.ClaimsIssuer!)
            ], Scheme.Name)), Scheme.Name)));
    }

    private sealed class FakeCoachRoster : ICoachRosterService
    {
        public List<string> Calls { get; } = [];
        public Error? Failure { get; init; }

        private Result<RosterDesk> Answer() => Failure is { } error ? Result<RosterDesk>.Failure(error.Code, error.Message) : Result<RosterDesk>.Success(
            new RosterDesk("open", SeasonGuid, "2026 Swan League", new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero), "2026-10-20T18:00", DateTimeOffset.UtcNow, false, null, false, 3, [new DraftTeam(TeamGuid, "Team 00", "Division 0", 1, true)]));

        public Task<Result<RosterDesk>> GetAsync(UserId coach, CancellationToken cancellationToken = default)
        {
            Calls.Add($"get:{coach.Value}");
            return Task.FromResult(Answer());
        }

        public Task<Result<RosterDesk>> SaveAsync(UserId coach, SaveRoster request, CancellationToken cancellationToken = default)
        {
            Calls.Add($"save:{coach.Value}:{request.SeasonId}:{request.ExpectedRevision}:{string.Join(",", request.TeamIds)}");
            return Task.FromResult(Answer());
        }

        public Task<Result<RosterDesk>> LockAsync(UserId coach, LockRoster request, CancellationToken cancellationToken = default)
        {
            Calls.Add($"lock:{coach.Value}:{request.SeasonId}:{request.ExpectedRevision}");
            return Task.FromResult(Answer());
        }
    }

    private sealed class FakeCommissionerRoster : ICommissionerRosterService
    {
        public List<string> Calls { get; } = [];
        public Error? Failure { get; init; }

        private Result<CommissionerDraft> Answer() => Failure is { } error ? Result<CommissionerDraft>.Failure(error.Code, error.Message) : Result<CommissionerDraft>.Success(
            new CommissionerDraft(SeasonGuid, "2026 Swan League", new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero), "2026-10-20T18:00", true, DateTimeOffset.UtcNow,
                [new CatalogEntry(TeamGuid, "Team 00", "Division 0", 3)], [new CommissionerCoach(MembershipGuid, "Coach A", "a@example.com", "missing", 0, [], [], false, null, 0)]));

        public Task<Result<CommissionerDraft>> GetAsync(UserId actor, CancellationToken cancellationToken = default)
        {
            Calls.Add($"get:{actor.Value}");
            return Task.FromResult(Answer());
        }

        public Task<Result<CommissionerDraft>> StartAsync(UserId actor, StartRosterDraft request, CancellationToken cancellationToken = default)
        {
            Calls.Add($"start:{actor.Value}:{request.SeasonId}:{request.RosterDraftDeadlineEastern:yyyy-MM-ddTHH:mm:ss}");
            return Task.FromResult(Answer());
        }

        public Task<Result<CommissionerDraft>> CorrectAsync(UserId actor, CorrectRoster request, CancellationToken cancellationToken = default)
        {
            Calls.Add($"correct:{actor.Value}:{request.MembershipId}:{request.ExpectedRevision}:{request.RemoveTeamId}:{request.AddTeamId}:{request.Reason}");
            return Task.FromResult(Answer());
        }

        public Task<Result<ReminderQueueResult>> QueueRemindersAsync(UserId actor, CancellationToken cancellationToken = default)
        {
            Calls.Add($"remind:{actor.Value}");
            return Task.FromResult(Result<ReminderQueueResult>.Success(new ReminderQueueResult(2)));
        }
    }
}
