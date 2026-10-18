using System.Net;
using Aspire.Hosting.Testing;
using Xunit;

namespace Swan.AppHost.Tests;

public sealed class AppHostSmokeTests
{
    [Fact]
    public async Task Api_becomes_healthy_when_the_distributed_application_starts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Swan_AppHost>(
            ["UseVolumes=false"],
            timeout.Token);
        await using var application = await builder.BuildAsync(timeout.Token);

        await application.StartAsync(timeout.Token);
        await application.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);

        using var client = application.CreateHttpClient("api");
        using var response = await client.GetAsync("/health", timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
