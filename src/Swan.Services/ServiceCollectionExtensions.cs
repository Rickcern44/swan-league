using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swan.Data;
using Swan.Data.Seed;
using Swan.Core.Integrations;
using Swan.Services.Integrations;

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
