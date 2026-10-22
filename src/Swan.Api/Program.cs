using Microsoft.AspNetCore.Authentication;
using Swan.Api.Authentication;
using Swan.Services;
using Swan.Api.Endpoints;
using Swan.Api.Endpoints.Admin;
using Swan.Api.Endpoints.Coach;
using Swan.Core.Identity;
using Swan.Core;
using Swan.Services.Development;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSwanServices();
if (builder.Environment.IsDevelopment())
{
    builder.Services.RemoveAll<IClock>();
    // Start from real time so browser session-cookie expiry remains valid; a simulation can then
    // deliberately advance this clock through its rehearsal timeline.
    builder.Services.AddSingleton(new DevelopmentClock(DateTimeOffset.UtcNow));
    builder.Services.AddSingleton<IClock>(serviceProvider => serviceProvider.GetRequiredService<DevelopmentClock>());
}
builder.Services.AddAuthentication("SwanSession")
    .AddScheme<AuthenticationSchemeOptions, SwanSessionAuthenticationHandler>("SwanSession", _ => { });
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, SetupManagerHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin)))
    .AddPolicy("SetupManager", policy => policy.RequireAuthenticatedUser().AddRequirements(new SetupManagerRequirement()))
    .AddPolicy("CoachOnly", policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Coach)))
    .AddPolicy("Coach", policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin), nameof(UserRole.Coach)));

var app = builder.Build();

if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("swan")))
{
    await app.MigrateSwanDatabaseAsync();
    if (app.Configuration.GetValue("Development:ResetOnStart", true)) await app.ResetAndSeedSwanDatabaseAsync();
}

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/ping", () => Results.Ok(new { message = "pong" })).AllowAnonymous();
app.MapIdentityEndpoints();
app.MapAuditEndpoints();
app.MapWeekEndpoints();
app.MapWeekPickEndpoints();
app.MapDashboardEndpoints();
app.MapCoachPickEndpoints();
app.MapRosterDraftEndpoints();
app.MapCommissionerRosterEndpoints();
app.MapSeasonEndpoints();
if (app.Environment.IsDevelopment()) app.MapDevelopmentEndpoints();

app.Run();

public partial class Program;
