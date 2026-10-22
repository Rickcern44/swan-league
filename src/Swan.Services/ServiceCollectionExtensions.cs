using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swan.Data;
using Swan.Data.Seed;
using Swan.Core.Integrations;
using Swan.Services.Integrations;
using Swan.Services.Identity;
using Swan.Services.League;
using Swan.Services.Platform;
using Swan.Services.Picks;
using Swan.Services.Schedule;
using Swan.Services.Scoring;
using Swan.Core;

namespace Swan.Services;

public static class ServiceCollectionExtensions
{
    public static TBuilder AddSwanServices<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddNpgsqlDbContext<SwanDbContext>("swan");
        builder.Services.AddSingleton<IEmailSender>(_ => new SmtpEmailSender(
            builder.Configuration["MAILPIT_HOST"] ?? "localhost",
            int.TryParse(builder.Configuration["MAILPIT_PORT"], out var port) ? port : 1025,
            "noreply@swan.local"));
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<MagicLinkService>();
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<ISetupAccessService, SetupAccessService>();
        builder.Services.AddScoped<Swan.Services.Development.DevelopmentResetService>();
        builder.Services.AddScoped<IAuditService, AuditService>();
        builder.Services.AddScoped<IWeekAdministrationService, WeekAdministrationService>();
        builder.Services.AddScoped<IAdminWeekPickService, AdminWeekPickService>();
        builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        builder.Services.AddScoped<Swan.Services.Picks.ICoachPickService, Swan.Services.Picks.CoachPickService>();
        builder.Services.AddScoped<Swan.Services.Picks.ICoachRosterService, Swan.Services.Picks.CoachRosterService>();
        builder.Services.AddScoped<Swan.Services.Picks.ICommissionerRosterService, Swan.Services.Picks.CommissionerRosterService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<ISeasonSetupService, SeasonSetupService>();
        // The key is read only from configuration (a .NET user secret in development); the typed client sends it in a header and never logs it.
        builder.Services.Configure<BallDontLieOptions>(builder.Configuration.GetSection(BallDontLieOptions.Section));
        builder.Services.AddHttpClient<BallDontLieScheduleProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.balldontlie.io/nfl/v1/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        builder.Services.AddScoped<IScheduleProvider>(serviceProvider => serviceProvider.GetRequiredService<BallDontLieScheduleProvider>());
        builder.Services.AddScoped<IStandingsProvider>(serviceProvider => serviceProvider.GetRequiredService<BallDontLieScheduleProvider>());
        builder.Services.AddScoped<IPreviousStandingsService, PreviousStandingsService>();
        builder.Services.AddScoped<StandingsService>();
        builder.Services.AddScoped<ICoachStandingsService, CoachStandingsService>();
        return builder;
    }

    public static async Task MigrateSwanDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SwanDbContext>().Database.MigrateAsync();
    }

    public static async Task SeedSwanDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        await DevelopmentSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<SwanDbContext>());
    }

    // Development always starts from the same usable baseline: the team catalog, test identities, and the 2025 reference season.
    // Season-scoped work from a prior run is deliberately discarded so setup and draft flows are exercised from scratch.
    public static async Task ResetAndSeedSwanDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SwanDbContext>();
        await DevelopmentSeeder.SeedAsync(database);
        await scope.ServiceProvider.GetRequiredService<Swan.Services.Development.DevelopmentResetService>()
            .ResetSeasonDataAsync(new Swan.Core.Common.UserId(Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d")));
        await DevelopmentSeeder.SeedAsync(database);
    }
}
