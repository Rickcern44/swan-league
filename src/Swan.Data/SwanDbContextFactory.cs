using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Swan.Data;

public sealed class SwanDbContextFactory : IDesignTimeDbContextFactory<SwanDbContext>
{
    private const string DefaultConnection = "Host=localhost;Port=5432;Database=swan;Username=postgres;Password=postgres";

    public SwanDbContext CreateDbContext(string[] args)
    {
        var connectionString = GetConnectionString(args);
        var options = new DbContextOptionsBuilder<SwanDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SwanDbContext(options);
    }

    internal static string GetConnectionString(string[] args)
    {
        var index = Array.IndexOf(args, "--connection");
        if (index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]))
            return args[index + 1];

        return Environment.GetEnvironmentVariable("SWAN_DESIGN_CONNECTION") is { Length: > 0 } configured
            ? configured
            : DefaultConnection;
    }
}
