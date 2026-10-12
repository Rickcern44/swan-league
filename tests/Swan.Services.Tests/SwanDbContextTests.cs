using Microsoft.EntityFrameworkCore;
using Swan.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Services.Tests;

public sealed class SwanDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task EnsureCreatedConnectsToPostgres()
    {
        var options = new DbContextOptionsBuilder<SwanDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var database = new SwanDbContext(options);

        Assert.True(await database.Database.EnsureCreatedAsync());
    }
}
