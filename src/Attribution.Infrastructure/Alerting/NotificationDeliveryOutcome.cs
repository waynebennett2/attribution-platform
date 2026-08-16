namespace Attribution.Infrastructure.Alerting;

public sealed record NotificationDeliveryOutcome(bool Success, string? FailureReason)
{
    public static readonly NotificationDeliveryOutcome Ok = new(true, null);

    public static NotificationDeliveryOutcome Failed(string reason) => new(false, reason);
}
