using Attribution.Application.Administration;
using Attribution.Domain.Audit;
using Attribution.Infrastructure.Alerting;
using Microsoft.Extensions.Options;

namespace Attribution.Workers.AlertingWorker;

// FR-047: evaluates ingestion lag, publication failure rate, allocation failure rate,
// pool utilisation and review-case age against configured thresholds, within 15 minutes
// (SC-017 — comfortably met by this worker's 1-minute tick). T092's AlertingService does
// the evaluation and persistence; this loop's own job is purely delivery — email, webhook,
// and recording whether each attempt succeeded so a stuck delivery pipeline is itself
// visible on integration health without suppressing the underlying alert (FR-047).
public sealed class AlertingWorker : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertingNotificationOptions _notificationOptions;
    private readonly ILogger<AlertingWorker> _logger;

    public AlertingWorker(
        IServiceScopeFactory scopeFactory, IOptions<AlertingNotificationOptions> notificationOptions, ILogger<AlertingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        _logger.LogInformation("AlertingWorker started with evaluation interval {Interval}", EvaluationInterval);

        do
        {
            try
            {
                await EvaluateAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertingWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EvaluateAndNotifyAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var alertingService = scope.ServiceProvider.GetRequiredService<AlertingService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IAlertEmailSender>();
        var webhookSender = scope.ServiceProvider.GetRequiredService<IAlertWebhookSender>();
        var deliveryStatusRepository = scope.ServiceProvider.GetRequiredService<INotificationDeliveryStatusRepository>();

        var events = await alertingService.EvaluateAsync(DateTimeOffset.UtcNow);
        foreach (var alertEvent in events)
        {
            await DispatchAsync(alertEvent, emailSender, webhookSender, deliveryStatusRepository, cancellationToken);
        }
    }

    private async Task DispatchAsync(
        AlertEvent alertEvent,
        IAlertEmailSender emailSender,
        IAlertWebhookSender webhookSender,
        INotificationDeliveryStatusRepository deliveryStatusRepository,
        CancellationToken cancellationToken)
    {
        var destination = _notificationOptions.For(alertEvent.Alert.ConditionType.ToString());
        var occurredAt = DateTimeOffset.UtcNow;

        if (destination.EmailRecipients.Length > 0)
        {
            var outcome = await emailSender.SendAsync(
                destination.EmailRecipients, BuildEmailSubject(alertEvent), BuildEmailBody(alertEvent), cancellationToken);
            await deliveryStatusRepository.RecordAttemptAsync(NotificationChannel.Email, outcome.Success, outcome.FailureReason, occurredAt);
            if (!outcome.Success)
            {
                _logger.LogWarning(
                    "Alert email delivery failed for alert {AlertId} ({ConditionType}): {Reason}",
                    alertEvent.Alert.Id, alertEvent.Alert.ConditionType, outcome.FailureReason);
            }
        }

        if (!string.IsNullOrWhiteSpace(destination.WebhookUrl))
        {
            var payload = BuildWebhookPayload(alertEvent, occurredAt);
            var outcome = await webhookSender.SendAsync(destination.WebhookUrl, payload, cancellationToken);
            await deliveryStatusRepository.RecordAttemptAsync(NotificationChannel.Webhook, outcome.Success, outcome.FailureReason, occurredAt);
            if (!outcome.Success)
            {
                _logger.LogWarning(
                    "Alert webhook delivery failed for alert {AlertId} ({ConditionType}) to {WebhookUrl}: {Reason}",
                    alertEvent.Alert.Id, alertEvent.Alert.ConditionType, destination.WebhookUrl, outcome.FailureReason);
            }
        }
    }

    private static string BuildEmailSubject(AlertEvent alertEvent) =>
        $"[Attribution Platform] {alertEvent.Status} alert: {ToSnakeCase(alertEvent.Alert.ConditionType)}";

    private static string BuildEmailBody(AlertEvent alertEvent) =>
        $"""
         Condition: {ToSnakeCase(alertEvent.Alert.ConditionType)}
         Scope: {alertEvent.Alert.ScopeRef ?? "(none)"}
         Status: {alertEvent.Status}
         Threshold: {alertEvent.Alert.Threshold}
         Current value: {alertEvent.CurrentValue}
         Raised at: {alertEvent.Alert.RaisedAt:O}
         """;

    private static AlertWebhookPayload BuildWebhookPayload(AlertEvent alertEvent, DateTimeOffset occurredAt) => new(
        AlertId: alertEvent.Alert.Id.ToString(),
        ConditionType: ToSnakeCase(alertEvent.Alert.ConditionType),
        Scope: BuildScope(alertEvent.Alert.ConditionType, alertEvent.Alert.ScopeRef),
        Status: alertEvent.Status.ToString().ToLowerInvariant(),
        Threshold: alertEvent.Alert.Threshold,
        CurrentValue: alertEvent.CurrentValue,
        RaisedAt: alertEvent.Alert.RaisedAt,
        OccurredAt: occurredAt);

    private static IReadOnlyDictionary<string, string?> BuildScope(AlertConditionType conditionType, string? scopeRef)
    {
        var key = conditionType switch
        {
            AlertConditionType.IngestionLag => "feed",
            AlertConditionType.PublicationFailureRate => "destination",
            AlertConditionType.PoolUtilisation => "pool_id",
            AlertConditionType.ReviewCaseAge => "review_case_id",
            AlertConditionType.AllocationFailureRate => "scope",
            _ => "scope",
        };
        return new Dictionary<string, string?> { [key] = scopeRef };
    }

    private static string ToSnakeCase(AlertConditionType conditionType) => conditionType switch
    {
        AlertConditionType.IngestionLag => "ingestion_lag",
        AlertConditionType.PublicationFailureRate => "publication_failure_rate",
        AlertConditionType.AllocationFailureRate => "allocation_failure_rate",
        AlertConditionType.PoolUtilisation => "pool_utilisation",
        AlertConditionType.ReviewCaseAge => "review_case_age",
        _ => conditionType.ToString(),
    };
}
