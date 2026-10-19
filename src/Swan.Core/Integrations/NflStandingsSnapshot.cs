namespace Swan.Core.Integrations;

// A normalized, immutable copy of a completed NFL season. DataJson is deliberately stored as the
// canonical flat-file shape so it can be exported and reused without a provider request.
public sealed class NflStandingsSnapshot
{
    private NflStandingsSnapshot() { }

    public NflStandingsSnapshot(Guid id, int season, string source, DateTimeOffset fetchedAt, string dataJson) =>
        (Id, Season, Source, FetchedAt, DataJson) = (id, season, source, fetchedAt, dataJson);

    public Guid Id { get; private set; }
    public int Season { get; private set; }
    public string Source { get; private set; } = "";
    public DateTimeOffset FetchedAt { get; private set; }
    public string DataJson { get; private set; } = "";
}
