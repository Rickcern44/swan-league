using Swan.Core.Common;

namespace Swan.Core.Schedule;

// What the dashboard needs to know about one week, already counted from its games (excluded games are never counted as needing anything).
public sealed record WeekSnapshot(WeekId Id, int Number, WeekState State, WeekKind Kind, int Games, int IncludedGames, int LinesApproved, int ResultsConfirmed, bool IsDoubleDogWeek);

// One thing for the administrator: a stable kind, readable reason, and where it is. NeedsDecision is false for pure status (picks are live).
public sealed record AttentionItem(string Kind, string Title, string Reason, WeekId WeekId, int WeekNumber, WeekState WeekState, int? Count, int? Of, bool NeedsDecision);

public sealed record AdminAttention(WeekSnapshot? CurrentWeek, IReadOnlyList<AttentionItem> Items, bool CaughtUp)
{
    // Ranking: decisions before status, then earlier weeks before later ones, so an older week still waiting on the admin outranks the newer current week.
    // Only the next Draft week (the lowest number) asks for lines; later Drafts are future work, not decisions yet.
    // The current week is the lowest Draft, LinesApproved or Open week, else the lowest Locked week, else the newest Final week.
    public static AdminAttention Derive(IReadOnlyCollection<WeekSnapshot> weeks)
    {
        var ordered = weeks.OrderBy(week => week.Number).ToList();
        var nextDraft = ordered.FirstOrDefault(week => week.State == WeekState.Draft);
        var items = new List<AttentionItem>();
        foreach (var week in ordered)
        {
            var item = week.State switch
            {
                WeekState.Draft when week.Id == nextDraft!.Id => DraftItem(week),
                WeekState.LinesApproved => new AttentionItem("publish-ready", "Ready to publish", $"Week {week.Number}'s lines are approved; publish it to open picks.", week.Id, week.Number, week.State, null, null, true),
                WeekState.Open => new AttentionItem("picks-live", "Picks are live", $"Week {week.Number} is open for picks.", week.Id, week.Number, week.State, null, null, false),
                WeekState.Locked => LockedItem(week),
                _ => null,
            };
            if (item is not null) items.Add(item);
        }

        var ranked = items.OrderByDescending(item => item.NeedsDecision).ThenBy(item => item.WeekNumber).ToList();
        var current = ordered.FirstOrDefault(week => week.State is WeekState.Draft or WeekState.LinesApproved or WeekState.Open)
            ?? ordered.FirstOrDefault(week => week.State == WeekState.Locked)
            ?? ordered.LastOrDefault(week => week.State == WeekState.Final);
        return new AdminAttention(current, ranked, ordered.Count > 0 && ranked.Count == 0);
    }

    private static AttentionItem DraftItem(WeekSnapshot week)
    {
        var missing = week.IncludedGames - week.LinesApproved;
        if (week.IncludedGames == 0) return new("week-empty", "Week has no games", $"Week {week.Number} has no included games to put lines on.", week.Id, week.Number, week.State, 0, 0, true);
        return missing > 0
            ? new("lines-missing", "Lines need approval", $"{missing} of {week.IncludedGames} included {Games(week.IncludedGames)} {(missing == 1 ? "needs" : "need")} a line", week.Id, week.Number, week.State, missing, week.IncludedGames, true)
            : new("lines-ready", "Lines are ready to approve", week.IncludedGames == 1 ? "The included game has an approved line; approve the week's lines." : $"All {week.IncludedGames} included games have an approved line; approve the week's lines.", week.Id, week.Number, week.State, 0, week.IncludedGames, true);
    }

    private static AttentionItem LockedItem(WeekSnapshot week)
    {
        var missing = week.IncludedGames - week.ResultsConfirmed;
        return missing > 0
            ? new("results-missing", "Results to confirm", $"{missing} of {week.IncludedGames} included {Games(week.IncludedGames)} {(missing == 1 ? "is missing a result" : "are missing results")}", week.Id, week.Number, week.State, missing, week.IncludedGames, true)
            : new("finalize-ready", "Ready to finalize", $"All results are in; finalize Week {week.Number} to score it.", week.Id, week.Number, week.State, 0, week.IncludedGames, true);
    }

    private static string Games(int count) => count == 1 ? "game" : "games";
}
