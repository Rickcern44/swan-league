using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Schedule;
using Swan.Data;
using Swan.Services.Schedule;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class AdminDashboardServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync();
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    [Fact]
    public async Task WithNoLiveSeasonTheDashboardSaysSoEvenWhenASetupSeasonExists()
    {
        await Seed(Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Season", false).Value!);

        var dashboard = await Get();

        Assert.Null(dashboard.Season);
        Assert.Null(dashboard.CurrentWeek);
        Assert.Empty(dashboard.Attention);
        Assert.False(dashboard.CaughtUp);
    }

    [Fact]
    public async Task TheDashboardReadsTheLiveSeasonsCurrentWeekAndCountsIncludedGamesOnly()
    {
        var season = Live(2026, "2026 Season");
        await Seed(season);
        var week = AddWeek(season, 1, games: 4, excluded: 1);
        await Persist(week);
        await Change(week.Id, w =>
        {
            foreach (var game in w.Games.Where(game => !game.Excluded).Take(2))
            {
                w.AddLine(game.Id, Spread.Create(-3).Value!);
                w.ApproveLatestLine(game.Id);
            }
        });

        var dashboard = await Get();

        Assert.Equal(("2026 Season", 2026, false), (dashboard.Season!.Name, dashboard.Season.Year, dashboard.Season.IsSimulation));
        Assert.Equal((1, WeekState.Draft, 4, 3, 2), (dashboard.CurrentWeek!.Number, dashboard.CurrentWeek.State, dashboard.CurrentWeek.Games, dashboard.CurrentWeek.IncludedGames, dashboard.CurrentWeek.LinesApproved));
        var item = Assert.Single(dashboard.Attention);
        Assert.Equal(("lines-missing", 1, 3), (item.Kind, item.Count, item.Of));
    }

    [Fact]
    public async Task TheNewestLiveSeasonYearWinsAndOtherSeasonsWeeksAreIgnored()
    {
        var older = Live(2025, "2025 Season");
        var newer = Live(2026, "2026 Season");
        await Seed(older, newer);
        await Persist(AddWeek(older, 1, 2), AddWeek(newer, 1, 2));
        await Persist(AddWeek(older, 2, 2));

        var dashboard = await Get();

        Assert.Equal(2026, dashboard.Season!.Year);
        Assert.Equal(1, dashboard.CurrentWeek!.Number);
        Assert.Single(dashboard.Attention);
    }

    [Fact]
    public async Task ALockedWeekWithEveryResultInIsReadyToFinalize()
    {
        var season = Live(2026, "2026 Season");
        await Seed(season);
        var week = AddWeek(season, 1, 1);
        await Persist(week);
        await Change(week.Id, w =>
        {
            var game = w.Games.Single();
            w.AddLine(game.Id, Spread.Create(-1).Value!);
            w.ApproveLatestLine(game.Id);
            w.ApproveLines();
            w.Publish();
            w.Lock();
            w.ConfirmResult(game.Id, GameResult.Create(10, 3).Value!);
        });
        var locked = await Get();
        Assert.Equal("finalize-ready", Assert.Single(locked.Attention).Kind);
    }

    private static Season Live(int year, string name)
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), year, name, false).Value!;
        Assert.True(season.ActivateWithDogs().IsSuccess);
        return season;
    }

    private static Week AddWeek(Season season, int number, int games, int excluded = 0)
    {
        var week = new Week(new WeekId(Guid.NewGuid()), season.Id, number, new WeekType(WeekKind.Regular, null, true));
        for (var index = 0; index < games; index++)
            week.AddGame(new Game(new GameId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new TeamId(Guid.NewGuid()), new Kickoff(DateTimeOffset.UtcNow.AddDays(3)), excluded: index < excluded));
        return week;
    }

    private async Task Seed(params Season[] seasons)
    {
        await using var database = CreateDatabase();
        database.Seasons.AddRange(seasons);
        await database.SaveChangesAsync();
    }

    private async Task Persist(params Week[] weeks)
    {
        await using var database = CreateDatabase();
        database.Weeks.AddRange(weeks);
        await database.SaveChangesAsync();
    }

    private async Task Change(WeekId id, Action<Week> change)
    {
        await using var database = CreateDatabase();
        var week = await database.Weeks.SingleAsync(week => week.Id == id);
        change(week);
        await database.SaveChangesAsync();
    }

    private async Task<AdminDashboard> Get()
    {
        await using var database = CreateDatabase();
        return (await new AdminDashboardService(database).GetAsync()).Value!;
    }

    private SwanDbContext CreateDatabase() => new(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
}
