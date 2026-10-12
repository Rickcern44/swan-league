using Swan.Core.Common;

namespace Swan.Core.Scoring;

public sealed record ScoreLine(TeamId TeamId, int Points);
public sealed class WeekScore(WeekScoreId id, SeasonId seasonId, MembershipId membershipId, WeekId weekId) : AggregateRoot
{
    private readonly List<ScoreLine> lines = [];
    public WeekScoreId Id { get; } = id;
    public SeasonId SeasonId { get; } = seasonId;
    public MembershipId MembershipId { get; } = membershipId;
    public WeekId WeekId { get; } = weekId;
    public IReadOnlyCollection<ScoreLine> Lines => lines.AsReadOnly();
    public Result<bool> ReplaceLines(IEnumerable<ScoreLine> value) { lines.Clear(); lines.AddRange(value); return Result<bool>.Success(true); }
}
