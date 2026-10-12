using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Swan.Data;

public sealed class SwanDbContextFactory : IDesignTimeDbContextFactory<SwanDbContext>
{
    public SwanDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Host=localhost;Port=5432;Database=swan;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<SwanDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SwanDbContext(options);
    }
}
