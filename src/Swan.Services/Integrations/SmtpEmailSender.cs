using System.Net;
using System.Net.Mail;
using Swan.Core.Integrations;

namespace Swan.Services.Integrations;

public sealed class SmtpEmailSender(string host, int port, string sender) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(host, port) { EnableSsl = false };
        using var mail = new MailMessage(sender, message.Recipient, message.Subject, message.Body) { IsBodyHtml = message.IsHtml };
        await client.SendMailAsync(mail, cancellationToken);
    }
}
