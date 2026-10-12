namespace Swan.Core.Integrations;

public sealed record EmailMessage(string Recipient, string Subject, string Body, bool IsHtml);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
