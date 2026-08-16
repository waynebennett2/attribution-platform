using Attribution.Application.Administration;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class NotificationDeliveryStatusRepository : RepositoryBase, INotificationDeliveryStatusRepository
{
    public NotificationDeliveryStatusRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task RecordAttemptAsync(NotificationChannel channel, bool succeeded, string? failureReason, DateTimeOffset at)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO notification_delivery_status (channel, last_attempt_at, last_success_at, last_failure_at, last_failure_reason)
            VALUES (@Channel, @At, @SuccessAt, @FailureAt, @FailureReason)
            ON DUPLICATE KEY UPDATE
                last_attempt_at = @At,
                last_success_at = COALESCE(@SuccessAt, last_success_at),
                last_failure_at = COALESCE(@FailureAt, last_failure_at),
                last_failure_reason = CASE WHEN @SuccessAt IS NOT NULL THEN NULL ELSE COALESCE(@FailureReason, last_failure_reason) END
            """,
            new
            {
                Channel = channel.ToString(),
                At = at,
                SuccessAt = succeeded ? at : (DateTimeOffset?)null,
                FailureAt = succeeded ? (DateTimeOffset?)null : at,
                FailureReason = succeeded ? null : failureReason,
            });
    }

    public async Task<IReadOnlyList<NotificationDeliveryStatus>> GetAllAsync()
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<NotificationDeliveryStatusRow>("SELECT * FROM notification_delivery_status");
        return rows.Select(r => new NotificationDeliveryStatus(
            Enum.Parse<NotificationChannel>(r.Channel), r.LastAttemptAt, r.LastSuccessAt, r.LastFailureAt, r.LastFailureReason)).ToList();
    }

    private sealed class NotificationDeliveryStatusRow
    {
        public string Channel { get; set; } = string.Empty;
        public DateTimeOffset LastAttemptAt { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public string? LastFailureReason { get; set; }
    }
}
