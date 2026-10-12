using Swan.Core.Common;
using Swan.Core.Picks;
using Swan.Core.Scoring;
using Xunit;

namespace Swan.Core.Tests;

public sealed class PicksScoringTests
{
    [Fact]
    public void BallotLocksChangesAndTracksEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var ballot = new Ballot(new BallotId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()));
        var pick = new TeamPick(new TeamId(Guid.NewGuid()), new GameId(Guid.NewGuid()), now.AddHours(1));
        Assert.True(ballot.AddPick(pick, now).IsSuccess);
        Assert.False(ballot.ChangePick(pick.TeamId, pick with { LockTime = now.AddHours(2) }, now.AddHours(1), "late").IsSuccess);
        Assert.Contains(ballot.DomainEvents, item => item is PickSubmitted);
    }

    [Fact]
    public void ScoreLinesReplaceAll()
    {
        var score = new WeekScore(new WeekScoreId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()), new WeekId(Guid.NewGuid()));
        score.ReplaceLines([new ScoreLine(new TeamId(Guid.NewGuid()), 1)]);
        score.ReplaceLines([new ScoreLine(new TeamId(Guid.NewGuid()), 2)]);
        Assert.Single(score.Lines);
        Assert.Equal(2, score.Lines.Single().Points);
    }
}
