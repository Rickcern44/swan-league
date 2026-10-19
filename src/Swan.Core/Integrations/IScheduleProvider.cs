using Swan.Core.Common;

namespace Swan.Core.Integrations;

// One regular-season game as the schedule provider reports it. Kickoff is null when the provider's timestamp could not be read.
public sealed record ProviderGame(string Id, int Week, string HomeTeam, string AwayTeam, DateTimeOffset? Kickoff);

public sealed record ProviderSchedule(DateTimeOffset FetchedAt, IReadOnlyList<ProviderGame> Games);

public interface IScheduleProvider
{
    // Errors: "schedule.config" (not configured) and "schedule.provider" (unreachable or unusable response); an unpublished season is a successful empty schedule.
    Task<Result<ProviderSchedule>> FetchRegularSeasonAsync(int season, CancellationToken cancellationToken = default);
}

// Standings remain separate from the schedule contract: the schedule feed is imported for a new
// season, whereas these are the completed regular-season results used to establish point values.
public sealed record ProviderStanding(string TeamName, string Conference, string Division, int Wins, int Losses, int Ties);

public sealed record ProviderStandings(DateTimeOffset FetchedAt, IReadOnlyList<ProviderStanding> Teams);

public interface IStandingsProvider
{
    Task<Result<ProviderStandings>> FetchRegularSeasonStandingsAsync(int season, CancellationToken cancellationToken = default);
}
