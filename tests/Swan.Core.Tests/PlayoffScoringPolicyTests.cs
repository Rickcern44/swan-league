using Swan.Core.Scoring;
using Xunit;

namespace Swan.Core.Tests;

public sealed class PlayoffScoringPolicyTests
{
    [Fact] public void QualificationAwardsThreeOnlyToPlayoffPool() => Assert.Equal(3, PlayoffScoringPolicy.QualificationBonus(true));
    [Theory] [InlineData("WC", 2)] [InlineData("DIV", 4)] [InlineData("CONF", 6)] [InlineData("SB", 8)] public void RoundMultipliersApply(string round, int multiplier) => Assert.Equal(3 * multiplier, PlayoffScoringPolicy.ScoreWin(3, round));
    [Fact] public void NumberOneSeedGetsByeCompensationOnlyAfterConferenceWin() { Assert.Equal(10, PlayoffScoringPolicy.ByeCompensation(true, true)); Assert.Equal(0, PlayoffScoringPolicy.ByeCompensation(true, false)); }
}
