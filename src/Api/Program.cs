var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapGet("/api/ping", () => Results.Ok(new { message = "pong" }));

app.Run();

public partial class Program;
