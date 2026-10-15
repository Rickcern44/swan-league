namespace Swan.Core.Schedule;

// The one thing an administrator can do next on a week, derived from counts the server already holds so the browser never re-implements lifecycle rules.
// CanAct is true only when a single call to an existing lifecycle endpoint is what moves the week on; otherwise Detail names what is left. The endpoints remain the authority and can still refuse.
public sealed record WeekNextAction(string Kind, string Label, string Detail, bool CanAct)
{
    public static WeekNextAction For(WeekSnapshot week)
    {
        var missingLines = week.IncludedGames - week.LinesApproved;
        var missingResults = week.IncludedGames - week.ResultsConfirmed;
        return week.State switch
        {
            WeekState.Draft when week.IncludedGames == 0 => new("week-empty", "No games to line", "This week has no included games.", false),
            WeekState.Draft when missingLines > 0 => new("add-lines", "Add lines", $"{missingLines} of {week.IncludedGames} included {Games(week.IncludedGames)} still {(missingLines == 1 ? "needs" : "need")} an approved line.", false),
            WeekState.Draft => new("approve-lines", $"Approve {week.IncludedGames} {(week.IncludedGames == 1 ? "line" : "lines")}", "Freezes the lines so the week can be published.", true),
            WeekState.LinesApproved => new("publish", "Publish week", "Opens the week for coach picks.", true),
            WeekState.Open => new("lock", "Lock week", "Closes picks so results can be entered.", true),
            WeekState.Locked when missingResults > 0 => new("enter-results", "Enter results", $"{missingResults} of {week.IncludedGames} included {Games(week.IncludedGames)} still {(missingResults == 1 ? "needs" : "need")} a result.", false),
            WeekState.Locked => new("finalize", "Finalize week", "Scores the week from the confirmed results.", true),
            _ => new("recompute", "Recompute scores", "The week is final. Recompute only to correct its scores.", true),
        };
    }

    private static string Games(int count) => count == 1 ? "game" : "games";
}
