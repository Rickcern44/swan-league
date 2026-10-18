using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swan.Core;
using Swan.Services.Integrations;
using Xunit;

namespace Swan.Services.Tests;

public sealed class BallDontLieScheduleProviderTests
{
    private const string Key = "test-key-should-never-leak";
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static string Game(int id, int week, string home, string away, string date = "2026-09-13T17:00:00.000Z", bool postseason = false) =>
        JsonSerializer.Serialize(new
        {
            id,
            week,
            season = 2026,
            postseason,
            date,
            status = "scheduled",
            home_team = new { id = 1, full_name = home, abbreviation = "HOM" },
            visitor_team = new { id = 2, full_name = away, abbreviation = "AWY" },
        });

    private static string Page(string[] games, int? next = null) =>
        "{\"data\":[" + string.Join(",", games) + "],\"meta\":{\"per_page\":100,\"next_cursor\":" + (next is null ? "null" : next.ToString()) + "}}";

    private static (BallDontLieScheduleProvider Provider, FakeHandler Handler) Create(string? key, params Func<HttpRequestMessage, HttpResponseMessage>[] replies)
    {
        var handler = new FakeHandler(replies);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.balldontlie.io/nfl/v1/") };
        return (new BallDontLieScheduleProvider(client, Options.Create(new BallDontLieOptions { ApiKey = key }), new FixedClock(), NullLogger<BallDontLieScheduleProvider>.Instance), handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AskedForTheSeasonItSendsTheKeyInTheAuthorizationHeaderAndRestrictsToRegularSeason()
    {
        var (provider, handler) = Create(Key, Json(Page([Game(1, 1, "Buffalo Bills", "New York Jets")])));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.True(fetched.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Key, request.Headers.Authorization!.ToString());
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        Assert.Contains("seasons[]=2026", query);
        Assert.Contains("season_types[]=2", query);
        Assert.Contains("per_page=100", query);
        Assert.DoesNotContain(Key, request.RequestUri.ToString());
        Assert.Equal("/nfl/v1/games", request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GamesAreMappedWithUtcKickoffsAndTheFetchTimeIsRecorded()
    {
        var (provider, _) = Create(Key, Json(Page([Game(77, 3, "Buffalo Bills", "New York Jets", "2026-09-27T20:25:00.000Z")])));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal(Now, fetched.Value!.FetchedAt);
        var game = Assert.Single(fetched.Value.Games);
        Assert.Equal(("77", 3, "Buffalo Bills", "New York Jets"), (game.Id, game.Week, game.HomeTeam, game.AwayTeam));
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 20, 25, 0, TimeSpan.Zero), game.Kickoff);
    }

    [Fact]
    public async Task EveryPageIsFetchedByFollowingTheCursorUntilItEnds()
    {
        var (provider, handler) = Create(Key,
            Json(Page([Game(1, 1, "A", "B")], next: 501)),
            Json(Page([Game(2, 1, "C", "D")], next: 902)),
            Json(Page([Game(3, 2, "E", "F")])));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3"], fetched.Value!.Games.Select(game => game.Id));
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain("cursor", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("cursor=501", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("cursor=902", handler.Requests[2].RequestUri!.Query);
    }

    [Fact]
    public async Task AnUnpublishedSeasonIsASuccessfulEmptySchedule()
    {
        var (provider, _) = Create(Key, Json(Page([])));

        var fetched = await provider.FetchRegularSeasonAsync(2031, TestContext.Current.CancellationToken);

        Assert.True(fetched.IsSuccess);
        Assert.Empty(fetched.Value!.Games);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutAKeyNoRequestIsMadeAndTheErrorIsConfiguration(string? key)
    {
        var (provider, handler) = Create(key);

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal("schedule.config", fetched.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ARejectedRequestIsAProviderErrorThatLeaksNeitherTheKeyNorTheBody(HttpStatusCode status)
    {
        var (provider, _) = Create(Key, Json($"secret provider body mentioning {Key}", status));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal("schedule.provider", fetched.Error!.Code);
        Assert.DoesNotContain(Key, fetched.Error.Message);
        Assert.DoesNotContain("secret provider body", fetched.Error.Message);
        Assert.Contains(((int)status).ToString(), fetched.Error.Message);
    }

    [Fact]
    public async Task ANetworkFailureIsAProviderError()
    {
        var (provider, _) = Create(Key, _ => throw new HttpRequestException($"connection refused for {Key}"));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal("schedule.provider", fetched.Error!.Code);
        Assert.DoesNotContain(Key, fetched.Error.Message);
    }

    [Fact]
    public async Task ATimeoutIsReportedAsPossibleRateLimitingNotAsUnreachable()
    {
        var (provider, _) = Create(Key, _ => throw new TaskCanceledException("The request was canceled due to the configured timeout."));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal("schedule.provider", fetched.Error!.Code);
        Assert.Contains("rate limit", fetched.Error.Message);
        Assert.DoesNotContain(Key, fetched.Error.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"data":"nope"}""")]
    [InlineData("""{"meta":{}}""")]
    public async Task AnUnusableResponseIsAProviderError(string body)
    {
        var (provider, _) = Create(Key, Json(body));

        Assert.Equal("schedule.provider", (await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken)).Error!.Code);
    }

    [Fact]
    public async Task APostseasonGameDespiteTheFilterIsAProviderError()
    {
        var (provider, _) = Create(Key, Json(Page([Game(9, 1, "A", "B", postseason: true)])));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal("schedule.provider", fetched.Error!.Code);
    }

    [Theory]
    [InlineData("tomorrow evening")]
    [InlineData("2026-09-13T17:00:00")]
    [InlineData("")]
    public async Task AKickoffThatCannotBeReadAsAnInstantIsKeptAsMissingNotGuessed(string date)
    {
        var (provider, _) = Create(Key, Json(Page([Game(1, 1, "A", "B", date)])));

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(fetched.Value!.Games).Kickoff);
    }

    [Fact]
    public async Task ACursorThatNeverEndsStopsWithAProviderError()
    {
        var (provider, handler) = Create(Key, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page([Game(1, 1, "A", "B")], next: 5)) });

        var fetched = await provider.FetchRegularSeasonAsync(2026, TestContext.Current.CancellationToken);

        Assert.Equal("schedule.provider", fetched.Error!.Code);
        Assert.InRange(handler.Requests.Count, 2, 100);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        private int next;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var reply = replies[Math.Min(next++, replies.Length - 1)];
            return Task.FromResult(reply(request));
        }
    }
}
