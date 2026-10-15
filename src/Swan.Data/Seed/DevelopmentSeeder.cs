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
    public static async Task SeedAsync(SwanDbContext database, CancellationToken cancellationToken = default)
    {
        if (await database.Teams.AnyAsync(cancellationToken)) return;

        var teams = await LoadTeams(cancellationToken);
        var seasonId = new SeasonId(Guid.Parse("3908b3ef-4b67-4cf8-890f-4d72a295c8aa"));
        var buyIn = Money.Create(100m, "USD").Value!;
        var season = new Season(seasonId, new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", buyIn)]);
        season.SaveDoubleDogWeeks([2, 12, 17]);
        season.Activate();

        var seededTeams = teams.Select((team, index) => new Team(new TeamId(DeterministicGuid(index)), team.Name, null, DateTimeOffset.UtcNow, team.Division)).ToList();
        database.Teams.AddRange(seededTeams);
        database.Seasons.Add(season);
        database.SeasonTeams.AddRange(teams.Select((team, index) => new SeasonTeam(new SeasonTeamId(DeterministicGuid(index + 100)), seasonId, seededTeams[index].Id, team.Division, team.Finish, 0)));

        var adminId = new UserId(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d"));
        database.Users.Add(new User(adminId, Email.Create("admin@swan.local").Value!, "Swan Admin", UserRole.Admin));
        var coaches = Enumerable.Range(1, 6).Select(index => new User(new UserId(DeterministicGuid(index + 200)), Email.Create($"coach{index}@swan.local").Value!, $"Coach {index}")).ToList();
        database.Users.AddRange(coaches);
        database.Memberships.Add(new Membership(new MembershipId(DeterministicGuid(300)), adminId, seasonId, MembershipRole.Administrator));
        database.Memberships.AddRange(coaches.Select((coach, index) => new Membership(new MembershipId(DeterministicGuid(index + 301)), coach.Id, seasonId, MembershipRole.Coach)));
        // Nine teams each (R1), spread so rosters overlap a little; a real league drafts these, which is not built yet.
        database.Rosters.AddRange(coaches.Select((coach, index) =>
        {
            var roster = new Roster(new RosterId(DeterministicGuid(index + 400)), seasonId, new MembershipId(DeterministicGuid(index + 301)));
            foreach (var offset in Enumerable.Range(0, 9)) roster.AddTeam(seededTeams[(index * 5 + offset) % seededTeams.Count].Id, windowOpen: true);
            return roster;
        }));

        var week = new Week(new WeekId(DeterministicGuid(500)), seasonId, 1, new WeekType(WeekKind.Regular, null, true));
        week.AddGame(new Game(new GameId(DeterministicGuid(501)), seededTeams[0].Id, seededTeams[1].Id, Kickoff.Create(DateTimeOffset.UtcNow.AddDays(7)).Value!));
        database.Weeks.Add(week);
        database.Weeks.Add(OpenDoubleDogWeek(seasonId, seededTeams));
        await database.SaveChangesAsync(cancellationToken);
    }

    // Week 2 is a double-dog week (R7). It is published with approved lines and dog options so a seeded coach has something to pick for.
    private static Week OpenDoubleDogWeek(SeasonId seasonId, IReadOnlyList<Team> seededTeams)
    {
        var week = new Week(new WeekId(DeterministicGuid(510)), seasonId, 2, new WeekType(WeekKind.Regular, null, true));
        decimal[] lines = [-3m, 2.5m, -7m, 1.5m];
        var first = DateTimeOffset.UtcNow.AddDays(7);
        for (var index = 0; index < 16; index++)
        {
            var game = new Game(new GameId(DeterministicGuid(511 + index)), seededTeams[index].Id, seededTeams[index + 16].Id, Kickoff.Create(first.AddDays(index / 4).AddHours(index % 4)).Value!);
            week.AddGame(game);
            week.AddLine(game.Id, Spread.Create(lines[index % 4]).Value!);
            week.ApproveLatestLine(game.Id);
        }

        week.GenerateDogOptions();
        week.ApproveLines();
        week.Publish();
        return week;
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
