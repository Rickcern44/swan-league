using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Swan.Api.Tests;

public sealed class PingEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetPingReturnsPong()
    {
        var response = await factory.CreateClient().GetAsync("/api/ping");

        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pong", payload.RootElement.GetProperty("message").GetString());
    }
}
