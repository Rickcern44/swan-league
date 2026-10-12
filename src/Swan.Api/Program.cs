using Swan.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSwanServices();

var app = builder.Build();

if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("swan")))
{
    await app.MigrateSwanDatabaseAsync();
}

app.MapDefaultEndpoints();
app.MapGet("/api/ping", () => Results.Ok(new { message = "pong" }));

app.Run();

public partial class Program;
