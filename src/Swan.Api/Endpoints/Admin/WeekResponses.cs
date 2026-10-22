using Swan.Core.Schedule;
using Swan.Services.Schedule;

namespace Swan.Api.Endpoints.Admin;

public sealed record LatestLineResponse(decimal Spread, int? HomeMoneyline, int? AwayMoneyline, string Source, DateTimeOffset EnteredAt, Guid EnteredBy);
public sealed record GameResponse(Guid Id, Guid HomeTeamId, Guid AwayTeamId, string HomeTeamName, string AwayTeamName, DateTimeOffset Kickoff, bool Excluded, decimal? ApprovedLine, IReadOnlyList<decimal> LineHistory, bool HasResult, int? HomeScore, int? AwayScore, LatestLineResponse? LatestLine);
public sealed record NextActionResponse(string Kind, string Label, string Detail, bool CanAct);
public sealed record WeekResponse(Guid Id, Guid SeasonId, int Number, string Kind, string State, IReadOnlyList<GameResponse> Games, int IncludedGames, int LinesApproved, int ResultsConfirmed, NextActionResponse NextAction, IReadOnlyList<Guid> DogOptionTeamIds)
{
    // A team the catalog does not know is said to be unknown rather than given an invented name.
    private const string UnknownTeam = "Unknown team";

    public static WeekResponse From(WeekDashboard week)
    {
        var included = week.Games.Where(game => !game.Excluded).ToList();
        var next = week.NextAction ?? new WeekNextAction("none", "", "", false);
        return new(
            week.Id.Value, week.SeasonId.Value, week.Number, week.Kind.ToString(), week.State.ToString(),
            week.Games.Select(game => new GameResponse(game.Id.Value, game.HomeTeamId.Value, game.AwayTeamId.Value, game.HomeTeamName ?? UnknownTeam, game.AwayTeamName ?? UnknownTeam, game.Kickoff, game.Excluded, game.ApprovedLine, game.LineHistory, game.HasResult, game.HomeScore, game.AwayScore, game.LatestLine is { } line ? new LatestLineResponse(line.Spread, line.HomeMoneyline, line.AwayMoneyline, line.Source.ToString().ToLowerInvariant(), line.EnteredAt, line.EnteredBy.Value) : null)).ToList(),
            included.Count, included.Count(game => game.ApprovedLine is not null), included.Count(game => game.HasResult),
            new NextActionResponse(next.Kind, next.Label, next.Detail, next.CanAct),
            week.DogOptions.Select(team => team.Value).ToList());
    }
}
