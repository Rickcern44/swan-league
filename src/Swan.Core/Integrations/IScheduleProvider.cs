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
