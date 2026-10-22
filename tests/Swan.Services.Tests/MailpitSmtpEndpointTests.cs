using Microsoft.Extensions.Configuration;
using Swan.Services.Integrations;
using Xunit;

namespace Swan.Services.Tests;

public sealed class MailpitSmtpEndpointTests
{
    [Fact]
    public void ReadsTheSmtpEndpointProvidedByAnAspireMailpitReference()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:mailpit"] = "endpoint=smtp://mailpit:1025"
            })
            .Build();

        var endpoint = MailpitSmtpEndpoint.FromConfiguration(configuration);

        Assert.Equal(("mailpit", 1025), (endpoint.Host, endpoint.Port));
    }
}
