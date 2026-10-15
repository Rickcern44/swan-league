using Microsoft.AspNetCore.Authentication;
using Swan.Api.Authentication;
using Swan.Services;
using Swan.Api.Endpoints;
using Swan.Api.Endpoints.Admin;
using Swan.Api.Endpoints.Coach;
using Swan.Core.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSwanServices();
builder.Services.AddAuthentication("SwanSession")
    .AddScheme<AuthenticationSchemeOptions, SwanSessionAuthenticationHandler>("SwanSession", _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin)))
    .AddPolicy("CoachOnly", policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Coach)))
    .AddPolicy("Coach", policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin), nameof(UserRole.Coach)));

var app = builder.Build();

if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("swan")))
{
    await app.MigrateSwanDatabaseAsync();
    await app.SeedSwanDatabaseAsync();
}

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/ping", () => Results.Ok(new { message = "pong" })).AllowAnonymous();
app.MapIdentityEndpoints();
app.MapAuditEndpoints();
app.MapWeekEndpoints();
app.MapDashboardEndpoints();
app.MapCoachPickEndpoints();
app.MapSeasonEndpoints();

app.Run();

public partial class Program;
