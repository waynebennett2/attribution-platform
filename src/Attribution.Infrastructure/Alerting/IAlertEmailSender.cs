namespace Attribution.Infrastructure.Alerting;

public interface IAlertEmailSender
{
    Task<NotificationDeliveryOutcome> SendAsync(
        IReadOnlyCollection<string> recipients, string subject, string body, CancellationToken cancellationToken);
}
