namespace Attribution.Infrastructure.Alerting;

public interface IAlertWebhookSender
{
    Task<NotificationDeliveryOutcome> SendAsync(string webhookUrl, AlertWebhookPayload payload, CancellationToken cancellationToken);
}
