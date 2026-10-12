namespace Swan.Core.Common;

public interface ITeamPickLockSettings
{
    TimeOnly? TeamPickDeadlineEastern { get; }
}

public sealed record ScoringTeamPick(TeamId TeamId, GameId GameId, int PointValue);
public sealed record ScoringDogPick(TeamId TeamId, GameId GameId);

public interface IScoringBallot
{
    string ScoringReference { get; }
    IReadOnlyCollection<ScoringTeamPick> ScoringPicks { get; }
    IReadOnlyCollection<ScoringDogPick> ScoringDogs { get; }
}
