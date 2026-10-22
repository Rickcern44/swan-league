using System.Net;
using System.Net.Mail;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
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

// Aspire's Mailpit resource supplies endpoint=smtp://host:port through ConnectionStrings:mailpit.
// Environment variables remain a convenient override for running the API outside Aspire.
public sealed record MailpitSmtpEndpoint(string Host, int Port)
{
    public static MailpitSmtpEndpoint FromConfiguration(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("mailpit");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
            if (values.TryGetValue("endpoint", out var value)
                && Uri.TryCreate(value?.ToString(), UriKind.Absolute, out var endpoint)
                && endpoint.Scheme.Equals("smtp", StringComparison.OrdinalIgnoreCase))
                return new MailpitSmtpEndpoint(endpoint.Host, endpoint.IsDefaultPort ? 25 : endpoint.Port);
        }

        return new MailpitSmtpEndpoint(
            configuration["MAILPIT_HOST"] ?? "localhost",
            int.TryParse(configuration["MAILPIT_PORT"], out var port) ? port : 1025);
    }
}
