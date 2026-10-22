using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Core.Integrations;
using Swan.Core.League;
using Swan.Data;
using Swan.Data.Seed;
using Swan.Services.Identity;
using Testcontainers.PostgreSql;
using Xunit;

namespace Swan.Api.Tests;

// These run the real authentication handler, real sign-in endpoints and a real database: nothing here overrides authentication.
public sealed class SessionApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    public CapturingEmailSender Email { get; } = new();
    public WebApplicationFactory<Program> Development { get; private set; } = null!;
    public WebApplicationFactory<Program> Production { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync();
        await using (var database = new SwanDbContext(new DbContextOptionsBuilder<SwanDbContext>().UseNpgsql(postgres.GetConnectionString()).Options))
        {
            await database.Database.MigrateAsync();
            await DevelopmentSeeder.SeedAsync(database);
            var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "Session test season", true).Value!;
            season.SaveDoubleDogWeeks([2, 12, 17]);
            season.CompleteSetup();
            season.Start(hasActiveCommissioner: true);
            database.Seasons.Add(season);
            database.Memberships.Add(new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.Parse("00000000-0000-0000-0000-000000000204")), season.Id, Swan.Core.League.MembershipRole.Coach));
            await database.SaveChangesAsync();
        }

        WebApplicationFactory<Program> Create(string environment) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:swan", postgres.GetConnectionString());
            builder.UseSetting("Development:ResetOnStart", "false");
            builder.ConfigureServices(services => services.AddSingleton<IEmailSender>(Email));
        });
        Development = Create("Development");
        Production = Create("Production");
    }

    public async ValueTask DisposeAsync()
    {
        await Development.DisposeAsync();
        await Production.DisposeAsync();
        await postgres.DisposeAsync();
    }

    public async Task<T> WithDatabase<T>(WebApplicationFactory<Program> factory, Func<SwanDbContext, SessionService, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SwanDbContext>(), scope.ServiceProvider.GetRequiredService<SessionService>());
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> messages = [];
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (messages) messages.Add(message);
        return Task.CompletedTask;
    }

    public string LinkFor(string email)
    {
        lock (messages) return messages.Last(message => message.Recipient == email).Body;
    }
}

public sealed class SecureSessionTests(SessionApiFixture fixture) : IClassFixture<SessionApiFixture>
{
    private static readonly Guid AdminId = Guid.Parse("b548d5d9-272d-4f4d-b2b4-f179ad8b289d");
    private static readonly Guid Coach1Id = Guid.Parse("00000000-0000-0000-0000-000000000201");

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private static HttpRequestMessage Get(string url, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie is not null) request.Headers.Add("Cookie", $"swan_session={cookie}");
        return request;
    }

    // Signs in the way a person does: asks for a link, opens it, and keeps the cookie the server sets.
    private async Task<(string Credential, string SetCookie)> SignInAsync(WebApplicationFactory<Program> factory, string email)
    {
        var client = Client(factory);
        (await client.PostAsJsonAsync("/api/sign-in-links", new { email })).EnsureSuccessStatusCode();
        var token = Regex.Match(fixture.Email.LinkFor(email), "token=([A-F0-9]+)").Groups[1].Value;
        var response = await client.GetAsync($"/api/sign-in-links/redeem?token={token}");
        response.EnsureSuccessStatusCode();
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("swan_session="));
        return (Regex.Match(setCookie, "swan_session=([^;]+)").Groups[1].Value, setCookie);
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/admin/dashboard")]
    [InlineData("/api/coach/picks/current")]
    public async Task AUserIdInTheCookieAuthenticatesNobodyOnAnyKindOfRoute(string url)
    {
        var client = Client(fixture.Development);

        foreach (var forged in new[] { Coach1Id.ToString(), AdminId.ToString(), Coach1Id.ToString("N"), Coach1Id.ToString().ToUpperInvariant() })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get(url, forged), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get(url, null), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task GuessedOrMalformedCredentialsAreRefused()
    {
        var client = Client(fixture.Development);

        foreach (var guess in new[] { "", " ", "0", new string('A', 64), new string('a', 64), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "not-a-token", "../../etc/passwd" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/me", guess), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SigningInWithAMagicLinkGivesAnOpaqueCookieThatWorksAndIsStoredOnlyAsAHash()
    {
        var (credential, setCookie) = await SignInAsync(fixture.Development, "coach1@swan.local");

        Assert.Matches("^[A-F0-9]{64}$", credential);
        Assert.NotEqual(Coach1Id.ToString(), credential);
        Assert.DoesNotContain(Coach1Id.ToString(), setCookie, StringComparison.OrdinalIgnoreCase);
        var me = await Client(fixture.Development).SendAsync(Get("/api/me", credential), TestContext.Current.CancellationToken);
        me.EnsureSuccessStatusCode();
        var body = await me.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Coach", body!["role"].ToString());
        var stored = await fixture.WithDatabase(fixture.Development, (database, _) => database.Sessions.AsNoTracking().Where(session => session.UserId == new UserId(Coach1Id)).ToListAsync());
        var row = Assert.Single(stored);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential))), row.TokenHash);
        Assert.NotEqual(credential, row.TokenHash);
        Assert.Null(row.RevokedAt);
        Assert.True(row.ExpiresAt > row.CreatedAt.AddDays(1) && row.ExpiresAt <= row.CreatedAt.AddDays(31));
        // The credential does not appear in any column of the table.
        var anywhere = await fixture.WithDatabase(fixture.Development, (database, _) => database.Database.SqlQuery<int>($"select count(*)::int as \"Value\" from identity.sessions s where s::text like {"%" + credential + "%"}").SingleAsync());
        Assert.Equal(0, anywhere);
    }

    [Fact]
    public async Task ARepeatedSignInLinkRequestReturnsARetryAfterInsteadOfPretendingToSendEmail()
    {
        var email = $"rate-limit-{Guid.NewGuid():N}@swan.local";
        await fixture.WithDatabase(fixture.Development, async (database, _) =>
        {
            database.Users.Add(new User(new UserId(Guid.NewGuid()), Email.Create(email).Value!, "Rate Limited Coach"));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            return true;
        });
        var client = Client(fixture.Development);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sign-in-links", new { email }, TestContext.Current.CancellationToken)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/sign-in-links", new { email }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task TheCookieIsHttpOnlySameSiteLaxAndBoundedAndSecureOutsideDevelopment()
    {
        var (_, development) = await SignInAsync(fixture.Development, "coach2@swan.local");
        var (_, production) = await SignInAsync(fixture.Production, "coach3@swan.local");

        foreach (var header in new[] { development, production })
        {
            Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
            var expires = DateTimeOffset.Parse(Regex.Match(header, "expires=([^;]+)", RegexOptions.IgnoreCase).Groups[1].Value);
            Assert.InRange((expires - DateTimeOffset.UtcNow).TotalDays, 1, 31);
        }

        Assert.DoesNotContain("secure", development.Replace("samesite", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", production, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RolesStaySeparatedForRealSessions()
    {
        var (admin, _) = await SignInAsync(fixture.Development, "admin@swan.local");
        var (coach, _) = await SignInAsync(fixture.Development, "coach4@swan.local");
        var client = Client(fixture.Development);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/admin/dashboard", admin), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Get("/api/admin/dashboard", coach), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/coach/picks/current", coach), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Get("/api/coach/picks/current", admin), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/me", admin), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SigningOutRevokesThatSessionOnlyAndClearsTheCookie()
    {
        var (first, _) = await SignInAsync(fixture.Development, "coach5@swan.local");
        var other = await fixture.WithDatabase(fixture.Development, (_, sessions) => sessions.CreateAsync(new UserId(Guid.Parse("00000000-0000-0000-0000-000000000205"))));
        var client = Client(fixture.Development);

        var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/sign-out");
        signOut.Headers.Add("Cookie", $"swan_session={first}");
        var response = await client.SendAsync(signOut, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("swan_session=;"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/me", first), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/me", other.Credential), TestContext.Current.CancellationToken)).StatusCode);
        var revoked = await fixture.WithDatabase(fixture.Development, (database, _) => database.Sessions.AsNoTracking().CountAsync(session => session.RevokedAt != null && session.UserId == new UserId(Guid.Parse("00000000-0000-0000-0000-000000000205"))));
        Assert.Equal(1, revoked);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/sign-out", null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AnExpiredRevokedOrDeactivatedSessionIsRefused()
    {
        var coach6 = new UserId(Guid.Parse("00000000-0000-0000-0000-000000000206"));
        var expired = await fixture.WithDatabase(fixture.Development, (_, sessions) => sessions.CreateAsync(coach6));
        var revoked = await fixture.WithDatabase(fixture.Development, (_, sessions) => sessions.CreateAsync(coach6));
        var live = await fixture.WithDatabase(fixture.Development, (_, sessions) => sessions.CreateAsync(coach6));
        var client = Client(fixture.Development);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/me", live.Credential), TestContext.Current.CancellationToken)).StatusCode);

        await fixture.WithDatabase(fixture.Development, async (database, _) =>
            await database.Database.ExecuteSqlAsync($"""update identity.sessions set "ExpiresAt" = now() - interval '1 minute' where "TokenHash" = {Hash(expired.Credential)}"""));
        await fixture.WithDatabase(fixture.Development, async (database, _) =>
            await database.Database.ExecuteSqlAsync($"""update identity.sessions set "RevokedAt" = now() where "TokenHash" = {Hash(revoked.Credential)}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/me", expired.Credential), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/me", revoked.Credential), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/me", live.Credential), TestContext.Current.CancellationToken)).StatusCode);

        await fixture.WithDatabase(fixture.Development, async (database, _) =>
            await database.Database.ExecuteSqlAsync($"""update identity.users set "IsActive" = false where "Id" = {coach6.Value}"""));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/api/me", live.Credential), TestContext.Current.CancellationToken)).StatusCode);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
