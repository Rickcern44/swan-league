using Swan.Core.Common;

namespace Swan.Core.Scoring;

public static class PlayoffScoringPolicy
{
    public static int QualificationBonus(bool qualified) => qualified ? 3 : 0;
    public static int ScoreWin(int seasonPointValue, string round) => seasonPointValue * (round.ToUpperInvariant() switch { "WC" => 2, "DIV" => 4, "CONF" => 6, "SB" => 8, _ => 0 });
    public static int ByeCompensation(bool isNumberOneSeed, bool wonConference) => isNumberOneSeed && wonConference ? 10 : 0;
}
