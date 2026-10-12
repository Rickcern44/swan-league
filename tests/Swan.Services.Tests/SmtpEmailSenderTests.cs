using Swan.Core.Integrations;
using Swan.Services.Integrations;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Swan.Services.Tests;

public sealed class SmtpEmailSenderTests : IAsyncLifetime
{
    private readonly IContainer mailpit = new ContainerBuilder("axllent/mailpit:v1.31.2")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1025))
        .Build();

    public Task InitializeAsync() => mailpit.StartAsync();
    public Task DisposeAsync() => mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task SmtpSenderDeliversMessageVisibleThroughMailpitApi()
    {
        var sender = new SmtpEmailSender(mailpit.Hostname, mailpit.GetMappedPublicPort(1025), "noreply@swan.local");
        await sender.SendAsync(new EmailMessage("coach@example.com", "Mailpit integration", "Welcome", false));

        using var client = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}") };
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await client.GetStringAsync("/api/v1/messages");
            if (response.Contains("Mailpit integration", StringComparison.Ordinal)) return;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("Mailpit did not expose the sent message through its API.");
    }
}
