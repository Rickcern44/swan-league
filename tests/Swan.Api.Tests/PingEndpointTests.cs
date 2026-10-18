using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swan.Core.Common;
using Swan.Core.Platform;
using Swan.Services.Platform;
using Xunit;

namespace Swan.Api.Tests;

public sealed class PingEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetPingReturnsPong()
    {
        var response = await factory.CreateClient().GetAsync("/api/ping", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("pong", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetMeWithoutSessionReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CoachGetsForbiddenFromAdminEndpoint()
    {
        using var client = CreateClientForRole("Coach");

        var response = await client.GetAsync("/api/admin/audit", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminGetsOkFromAdminEndpoint()
    {
        using var client = CreateClientForRole("Admin");

        var response = await client.GetAsync("/api/admin/audit", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private HttpClient CreateClientForRole(string role) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => options.ClaimsIssuer = role);
        services.AddSingleton<IAuditService, TestAuditService>();
    })).CreateClient();

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.Parse("9c9b2c10-a312-4b77-a26a-ef546e87f3c9").ToString()),
                new Claim(ClaimTypes.Role, Options.ClaimsIssuer!)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestAuditService : IAuditService
    {
        public Task AppendAsync(UserId actor, string action, string resourceId, object? before = null, object? after = null, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    }
}
