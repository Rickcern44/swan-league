using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Schedule;

namespace Swan.Data.Seed;

public static class DevelopmentSeeder
{
    private static readonly SeasonId ReferenceSeasonId = new(Guid.Parse("220ceec6-d804-43b6-974b-d46b974f35b4"));
    private static readonly UserId AdminId = new(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d"));

    public static async Task SeedAsync(SwanDbContext database, CancellationToken cancellationToken = default)
    {
        if (!await database.Teams.AnyAsync(cancellationToken))
        {
            var teams = await LoadTeams(cancellationToken);
            database.Teams.AddRange(teams.Select((team, index) => new Team(new TeamId(DeterministicGuid(index)), team.Name, null, DateTimeOffset.UtcNow, team.Division)));
        }

        if (!await database.Users.AnyAsync(user => user.Id == AdminId, cancellationToken))
            database.Users.Add(new User(AdminId, Email.Create("admin@swan.local").Value!, "Swan Admin", UserRole.Admin));
        var coaches = Enumerable.Range(1, 6).Select(index => new User(new UserId(DeterministicGuid(index + 200)), Email.Create($"coach{index}@swan.local").Value!, $"Coach {index}")).ToList();
        var knownCoachIds = await database.Users.Where(user => coaches.Select(coach => coach.Id).Contains(user.Id)).Select(user => user.Id).ToListAsync(cancellationToken);
        database.Users.AddRange(coaches.Where(coach => !knownCoachIds.Contains(coach.Id)));

        if (!await database.Seasons.AnyAsync(season => season.Id == ReferenceSeasonId, cancellationToken))
            database.Seasons.Add(CreateReferenceSeason());
        if (!await database.Memberships.AnyAsync(membership => membership.Id == new MembershipId(DeterministicGuid(320)), cancellationToken))
        {
            database.Memberships.AddRange(
            [
                new Membership(new MembershipId(DeterministicGuid(320)), AdminId, ReferenceSeasonId, MembershipRole.Coach),
                new Membership(new MembershipId(DeterministicGuid(321)), coaches[0].Id, ReferenceSeasonId, MembershipRole.Coach),
            ]);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static Season CreateReferenceSeason()
    {
        var season = Season.CreateBlank(ReferenceSeasonId, 2025, "2025 Swan League", isSimulation: true).Value!;
        season.SaveDoubleDogWeeks([2, 12, 17]);
        season.CompleteSetup();
        season.Start(hasActiveCommissioner: true);
        season.Complete();
        return season;
    }

    private static async Task<List<NflTeamSeed>> LoadTeams(CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "nfl-2025-finishes.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<NflTeamSeed>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidOperationException("NFL seed data is missing.");
    }

    private static Guid DeterministicGuid(int value) => new($"00000000-0000-0000-0000-{value:D12}");

    private sealed record NflTeamSeed(string Name, string Division, int Finish);
}
