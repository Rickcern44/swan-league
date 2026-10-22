using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Integrations;

namespace Swan.Services.Integrations;

public sealed class BallDontLieOptions
{
    public const string Section = "ExternalServices:BallDontLie";

    public string? ApiKey { get; set; }
}

// Reads the NFL regular-season schedule from BALLDONTLIE. The key travels only in the Authorization header and is never logged or returned.
public sealed partial class BallDontLieScheduleProvider(HttpClient http, IOptions<BallDontLieOptions> options, IClock clock, ILogger<BallDontLieScheduleProvider> logger) : IScheduleProvider, IStandingsProvider, IFinalScoreProvider
{
    private const int PerPage = 100;
    private const int MaxPages = 50;
    private static readonly string[] KickoffFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    public async Task<Result<ProviderSchedule>> FetchRegularSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Result<ProviderSchedule>.Failure("schedule.config", "The schedule provider is not configured.");
        var games = new List<ProviderGame>();
        long? cursor = null;
        try
        {
            for (var page = 0; page < MaxPages; page++)
            {
                var query = $"games?seasons[]={season}&season_types[]=2&per_page={PerPage}{(cursor is null ? "" : $"&cursor={cursor}")}";
                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(key.Trim());
                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("BALLDONTLIE answered the schedule request with HTTP {Status}.", (int)response.StatusCode);
                    return Unavailable($"The schedule provider answered HTTP {(int)response.StatusCode}.");
                }

                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return Unavailable("The schedule provider returned an unexpected response.");
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("postseason", out var postseason) && postseason.ValueKind == JsonValueKind.True)
                        return Unavailable("The schedule provider returned a postseason game for a regular-season request.");
                    games.Add(Map(item));
                }

                cursor = document.RootElement.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.Number ? next.GetInt64() : null;
                if (cursor is null) return Result<ProviderSchedule>.Success(new ProviderSchedule(clock.UtcNow, games));
            }

            return Unavailable("The schedule provider returned too many pages.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The schedule request timed out.");
            return Unavailable("The schedule provider timed out; it may be rate limiting requests. Try again in a minute.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            // Only the exception type is logged: messages can quote request details.
            logger.LogWarning("The schedule request failed: {Type}.", exception.GetType().Name);
            return Unavailable("The schedule provider could not be reached or returned an unusable response.");
        }
    }

    public async Task<Result<ProviderStandings>> FetchRegularSeasonStandingsAsync(int season, CancellationToken cancellationToken = default)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Result<ProviderStandings>.Failure("standings.config", "The standings provider is not configured.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"standings?season={season}");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(key.Trim());
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("BALLDONTLIE answered the standings request with HTTP {Status}.", (int)response.StatusCode);
                return StandingsUnavailable($"The standings provider answered HTTP {(int)response.StatusCode}.");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return StandingsUnavailable("The standings provider returned an unexpected response.");

            var teams = data.EnumerateArray().Select(MapStanding).ToList();
            return Result<ProviderStandings>.Success(new ProviderStandings(clock.UtcNow, teams));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The standings request timed out.");
            return StandingsUnavailable("The standings provider timed out; it may be rate limiting requests. Try again in a minute.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            logger.LogWarning("The standings request failed: {Type}.", exception.GetType().Name);
            return StandingsUnavailable("The standings provider could not be reached or returned an unusable response.");
        }
    }

    public async Task<Result<ProviderFinalScores>> FetchFinalScoresAsync(int season, int week, CancellationToken cancellationToken = default)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Result<ProviderFinalScores>.Failure("scores.config", "The final-score provider is not configured.");
        var scores = new List<ProviderFinalScore>();
        long? cursor = null;
        try
        {
            for (var page = 0; page < MaxPages; page++)
            {
                var query = $"games?seasons[]={season}&weeks[]={week}&season_types[]=2&per_page={PerPage}{(cursor is null ? "" : $"&cursor={cursor}")}";
                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(key.Trim());
                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("BALLDONTLIE answered the final-score request with HTTP {Status}.", (int)response.StatusCode);
                    return ScoresUnavailable($"The final-score provider answered HTTP {(int)response.StatusCode}.");
                }

                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return ScoresUnavailable("The final-score provider returned an unexpected response.");
                foreach (var item in data.EnumerateArray())
                    if (item.TryGetProperty("status_state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "final")
                        scores.Add(MapFinalScore(item));

                cursor = document.RootElement.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.Number ? next.GetInt64() : null;
                if (cursor is null) return Result<ProviderFinalScores>.Success(new ProviderFinalScores(clock.UtcNow, scores));
            }

            return ScoresUnavailable("The final-score provider returned too many pages.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The final-score request timed out.");
            return ScoresUnavailable("The final-score provider timed out; it may be rate limiting requests. Try again in a minute.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            logger.LogWarning("The final-score request failed: {Type}.", exception.GetType().Name);
            return ScoresUnavailable("The final-score provider could not be reached or returned an unusable response.");
        }
    }

    // K would read a timestamp without an offset as local time; a kickoff must say which instant it is.
    [GeneratedRegex(@"(Z|[+-]\d\d:\d\d)$")]
    private static partial Regex HasOffset();

    private static ProviderGame Map(JsonElement item) => new(
        item.TryGetProperty("id", out var id) ? id.ToString() : "",
        item.TryGetProperty("week", out var week) && week.ValueKind == JsonValueKind.Number ? week.GetInt32() : 0,
        TeamName(item, "home_team"),
        TeamName(item, "visitor_team"),
        item.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.String && HasOffset().IsMatch(date.GetString() ?? "") && DateTimeOffset.TryParseExact(date.GetString(), KickoffFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var kickoff) ? kickoff.ToUniversalTime() : null);

    private static ProviderStanding MapStanding(JsonElement item)
    {
        var team = item.TryGetProperty("team", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        string String(string property) => team.ValueKind == JsonValueKind.Object && team.TryGetProperty(property, out var propertyValue) && propertyValue.ValueKind == JsonValueKind.String ? propertyValue.GetString() ?? "" : "";
        int Number(string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : -1;
        return new ProviderStanding(String("full_name"), String("conference"), String("division"), Number("wins"), Number("losses"), Number("ties"));
    }

    private static ProviderFinalScore MapFinalScore(JsonElement item)
    {
        string String(string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        int Number(string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : throw new FormatException($"Final-score field {property} is missing.");
        var id = item.TryGetProperty("id", out var providerId) ? providerId.ToString() : "";
        if (string.IsNullOrWhiteSpace(id)) throw new FormatException("Final-score id is missing.");
        var updatedAt = String("updated_at");
        if (!HasOffset().IsMatch(updatedAt) || !DateTimeOffset.TryParseExact(updatedAt, KickoffFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)) throw new FormatException("Final-score updated_at is invalid.");
        return new(id, Number("week"), TeamName(item, "home_team"), TeamName(item, "visitor_team"), Number("home_team_score"), Number("visitor_team_score"), updated.ToUniversalTime());
    }

    private static string TeamName(JsonElement item, string property) =>
        item.TryGetProperty(property, out var team) && team.ValueKind == JsonValueKind.Object && team.TryGetProperty("full_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";

    private static Result<ProviderSchedule> Unavailable(string message) => Result<ProviderSchedule>.Failure("schedule.provider", message);
    private static Result<ProviderStandings> StandingsUnavailable(string message) => Result<ProviderStandings>.Failure("standings.provider", message);
    private static Result<ProviderFinalScores> ScoresUnavailable(string message) => Result<ProviderFinalScores>.Failure("scores.provider", message);
}
