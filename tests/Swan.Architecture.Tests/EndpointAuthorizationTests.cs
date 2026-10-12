using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Swan.Architecture.Tests;

public sealed class EndpointAuthorizationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void EveryApiEndpointExplicitlyDeclaresAuthorization()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);

        Assert.All(endpoints, endpoint => Assert.True(
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null || endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null,
            $"{endpoint.RoutePattern.RawText} must explicitly allow anonymous access or require a policy."));
    }
}
