using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Reparatio.Repairs.Api;

public sealed class IdentityEmailSender : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var host = Environment.GetEnvironmentVariable("REPARATIO_SMTP_HOST");
        var from = Environment.GetEnvironmentVariable("REPARATIO_SMTP_FROM");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Email delivery must be configured before registration or password recovery.");
        using var client = new SmtpClient(host, int.Parse(Environment.GetEnvironmentVariable("REPARATIO_SMTP_PORT") ?? "587")) { EnableSsl = true };
        var user = Environment.GetEnvironmentVariable("REPARATIO_SMTP_USER");
        if (!string.IsNullOrEmpty(user)) client.Credentials = new NetworkCredential(user, Environment.GetEnvironmentVariable("REPARATIO_SMTP_PASSWORD"));
        using var message = new MailMessage(from, email, subject, htmlMessage) { IsBodyHtml = true };
        await client.SendMailAsync(message);
    }
}
