using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Integrations;
using Swan.Core.Schedule;
using Swan.Data;

namespace Swan.Services.Integrations;

public sealed record ArchivedStanding(string Name, string Division, int Finish);

public interface IPreviousStandingsService
{
    Task<Result<IReadOnlyList<TeamFinish>>> GetAsync(int previousSeason, IReadOnlyList<Team> catalog, CancellationToken cancellationToken = default);
}

// Read order is deliberately deterministic: checked-in archive, durable database cache, provider.
// A provider response is persisted only after it has mapped to every local NFL team and produced
// one unambiguous finish in every division.
public sealed class PreviousStandingsService(SwanDbContext database, IStandingsProvider provider) : IPreviousStandingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<IReadOnlyList<TeamFinish>>> GetAsync(int previousSeason, IReadOnlyList<Team> catalog, CancellationToken cancellationToken = default)
    {
        var archive = await ReadArchiveAsync(previousSeason, cancellationToken);
        if (archive is not null) return Map(archive, catalog);

        var cached = await database.NflStandingsSnapshots.AsNoTracking().SingleOrDefaultAsync(snapshot => snapshot.Season == previousSeason, cancellationToken);
        if (cached is not null)
        {
            var rows = JsonSerializer.Deserialize<List<ArchivedStanding>>(cached.DataJson, JsonOptions);
            return rows is null ? Invalid("The cached standings data is unreadable.") : Map(rows, catalog);
        }

        var fetched = await provider.FetchRegularSeasonStandingsAsync(previousSeason, cancellationToken);
        if (!fetched.IsSuccess) return Result<IReadOnlyList<TeamFinish>>.Failure(fetched.Error!.Code, fetched.Error.Message);
        var normalized = Normalize(fetched.Value!.Teams);
        if (!normalized.IsSuccess) return Result<IReadOnlyList<TeamFinish>>.Failure(normalized.Error!.Code, normalized.Error.Message);
        var mapped = Map(normalized.Value!, catalog);
        if (!mapped.IsSuccess) return mapped;

        database.NflStandingsSnapshots.Add(new NflStandingsSnapshot(Guid.NewGuid(), previousSeason, "balldontlie", fetched.Value.FetchedAt, JsonSerializer.Serialize(normalized.Value!, JsonOptions)));
        await database.SaveChangesAsync(cancellationToken);
        return mapped;
    }

    private static async Task<List<ArchivedStanding>?> ReadArchiveAsync(int season, CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", $"nfl-{season}-finishes.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ArchivedStanding>>(stream, JsonOptions, cancellationToken);
    }

    private static Result<IReadOnlyList<TeamFinish>> Map(IReadOnlyList<ArchivedStanding> rows, IReadOnlyList<Team> catalog)
    {
        var byName = catalog.ToDictionary(team => team.Name, StringComparer.OrdinalIgnoreCase);
        if (rows.Count != catalog.Count || rows.Any(row => !byName.ContainsKey(row.Name))) return Invalid("The standings do not match the league's team catalog.");
        var finishes = rows.Select(row => new TeamFinish(byName[row.Name].Id, row.Finish)).ToList();
        return SetupTeamFinishes.Create(new SeasonId(Guid.Empty), catalog, finishes).IsSuccess
            ? Result<IReadOnlyList<TeamFinish>>.Success(finishes)
            : Invalid("The standings do not contain one valid finish for every division.");
    }

    private static Result<List<ArchivedStanding>> Normalize(IReadOnlyList<ProviderStanding> providerRows)
    {
        if (providerRows.Any(row => string.IsNullOrWhiteSpace(row.TeamName) || row.Wins < 0 || row.Losses < 0 || row.Ties < 0)) return Result<List<ArchivedStanding>>.Failure("standings.invalid", "The standings provider returned incomplete data.");
        var rows = providerRows.Select(row => new { row, Division = $"{row.Conference.ToUpperInvariant()} {ToTitle(row.Division)}" }).ToList();
        if (rows.GroupBy(row => row.Division).Count() != 8 || rows.Any(row => row.Division.Length < 6) || rows.Any(row => string.IsNullOrWhiteSpace(row.row.TeamName))) return Result<List<ArchivedStanding>>.Failure("standings.invalid", "The standings provider returned invalid divisions.");
        if (rows.GroupBy(row => row.Division).Any(group => group.Count() != 4 || group.GroupBy(item => (item.row.Wins, item.row.Losses, item.row.Ties)).Any(tie => tie.Count() > 1))) return Result<List<ArchivedStanding>>.Failure("standings.ambiguous", "The provider cannot determine an unambiguous division finish. Enter that division manually.");
        return Result<List<ArchivedStanding>>.Success(rows.GroupBy(row => row.Division).SelectMany(group => group.OrderByDescending(item => item.row.Wins).ThenBy(item => item.row.Losses).ThenByDescending(item => item.row.Ties).Select((item, index) => new ArchivedStanding(item.row.TeamName, item.Division, index + 1))).ToList());
    }

    private static string ToTitle(string value) => string.IsNullOrWhiteSpace(value) ? "" : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    private static Result<IReadOnlyList<TeamFinish>> Invalid(string message) => Result<IReadOnlyList<TeamFinish>>.Failure("standings.invalid", message);
}
