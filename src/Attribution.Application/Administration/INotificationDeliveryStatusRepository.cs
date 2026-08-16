namespace Attribution.Application.Administration;

public enum NotificationChannel
{
    Email,
    Webhook,
}

public sealed record NotificationDeliveryStatus(
    NotificationChannel Channel,
    DateTimeOffset LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastFailureReason);

// FR-047's delivery-failure surfacing: one row per channel, upserted on every send attempt
// so AdminHealthController (FR-034) can show a stuck delivery pipeline independently of
// whether the underlying alert conditions themselves are healthy.
public interface INotificationDeliveryStatusRepository
{
    Task RecordAttemptAsync(NotificationChannel channel, bool succeeded, string? failureReason, DateTimeOffset at);

    Task<IReadOnlyList<NotificationDeliveryStatus>> GetAllAsync();
}
