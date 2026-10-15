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
        builder.Services.AddScoped<IAuditService, AuditService>();
        builder.Services.AddScoped<IWeekAdministrationService, WeekAdministrationService>();
        builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        builder.Services.AddScoped<Swan.Services.Picks.ICoachPickService, Swan.Services.Picks.CoachPickService>();
        builder.Services.AddScoped<Swan.Services.Picks.ICoachRosterService, Swan.Services.Picks.CoachRosterService>();
        builder.Services.AddScoped<Swan.Services.Picks.ICommissionerRosterService, Swan.Services.Picks.CommissionerRosterService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<ISeasonSetupService, SeasonSetupService>();
        // The key is read only from configuration (a .NET user secret in development); the typed client sends it in a header and never logs it.
        builder.Services.Configure<BallDontLieOptions>(builder.Configuration.GetSection(BallDontLieOptions.Section));
        builder.Services.AddHttpClient<IScheduleProvider, BallDontLieScheduleProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.balldontlie.io/nfl/v1/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        builder.Services.AddScoped<StandingsService>();
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
}
