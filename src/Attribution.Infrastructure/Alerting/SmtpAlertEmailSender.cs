using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Attribution.Infrastructure.Alerting;

// System.Net.Mail.SmtpClient rather than a third-party mail library: this repository has no
// other outbound-mail dependency, and FR-047's requirement is "an outbound mail path is
// available to the deployment" (spec.md Dependencies) — a plain SMTP relay is the common
// case for that, and adding a package for it isn't warranted by anything this increment
// needs beyond what the BCL already provides.
public sealed class SmtpAlertEmailSender : IAlertEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpAlertEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task<NotificationDeliveryOutcome> SendAsync(
        IReadOnlyCollection<string> recipients, string subject, string body, CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return NotificationDeliveryOutcome.Ok;
        }

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl };
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            using var message = new MailMessage { From = new MailAddress(_options.FromAddress), Subject = subject, Body = body };
            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            await client.SendMailAsync(message, cancellationToken);
            return NotificationDeliveryOutcome.Ok;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            return NotificationDeliveryOutcome.Failed(ex.Message);
        }
    }
}
