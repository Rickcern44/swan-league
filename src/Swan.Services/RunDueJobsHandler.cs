using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swan.Core;
using Swan.Core.Integrations;
using Swan.Data;

namespace Swan.Services;

public interface IRunDueJobsHandler
{
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed class NoOpRunDueJobsHandler : IRunDueJobsHandler
{
    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class QueuedEmailRunDueJobsHandler(IServiceScopeFactory scopes, IEmailSender emailSender, IClock clock) : IRunDueJobsHandler
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SwanDbContext>();
        var emails = await database.QueuedEmails.Where(email => email.SentAt == null).OrderBy(email => email.QueuedAt).Take(25).ToListAsync(cancellationToken);
        foreach (var email in emails)
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(email.Recipient, email.Subject, email.Body, false), cancellationToken);
                email.MarkSent(clock.UtcNow);
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Leave the failed message unsent so a later poll can retry it; a send failure does not affect the other queued messages.
            }
        }
    }
}
