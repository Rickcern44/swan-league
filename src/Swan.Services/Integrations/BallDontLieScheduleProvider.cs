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
public sealed partial class BallDontLieScheduleProvider(HttpClient http, IOptions<BallDontLieOptions> options, IClock clock, ILogger<BallDontLieScheduleProvider> logger) : IScheduleProvider
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
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            // Only the exception type is logged: messages can quote request details.
            logger.LogWarning("The schedule request failed: {Type}.", exception.GetType().Name);
            return Unavailable("The schedule provider could not be reached or returned an unusable response.");
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

    private static string TeamName(JsonElement item, string property) =>
        item.TryGetProperty(property, out var team) && team.ValueKind == JsonValueKind.Object && team.TryGetProperty("full_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";

    private static Result<ProviderSchedule> Unavailable(string message) => Result<ProviderSchedule>.Failure("schedule.provider", message);
}
