using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Audit;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Alerting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Alerts. FR-047.
[ApiController]
[Route("v1/admin/alerts")]
[Authorize]
public sealed class AdminAlertsController : ControllerBase
{
    private readonly IAlertRepository _alertRepository;
    private readonly AlertingService _alertingService;
    private readonly IAlertEmailSender _emailSender;
    private readonly IAlertWebhookSender _webhookSender;
    private readonly INotificationDeliveryStatusRepository _deliveryStatusRepository;
    private readonly AlertingNotificationOptions _notificationOptions;
    private readonly IActorContext _actorContext;
    private readonly IAuditLogger _auditLogger;

    public AdminAlertsController(
        IAlertRepository alertRepository,
        AlertingService alertingService,
        IAlertEmailSender emailSender,
        IAlertWebhookSender webhookSender,
        INotificationDeliveryStatusRepository deliveryStatusRepository,
        IOptions<AlertingNotificationOptions> notificationOptions,
        IActorContext actorContext,
        IAuditLogger auditLogger)
    {
        _alertRepository = alertRepository;
        _alertingService = alertingService;
        _emailSender = emailSender;
        _webhookSender = webhookSender;
        _deliveryStatusRepository = deliveryStatusRepository;
        _notificationOptions = notificationOptions.Value;
        _actorContext = actorContext;
        _auditLogger = auditLogger;
    }

    [HttpGet]
    [RequireOperation(Operation.AcknowledgeAlerts)]
    public async Task<IActionResult> ListOpen()
    {
        var alerts = await _alertRepository.GetOpenAsync();
        return Ok(alerts.Select(ToDto));
    }

    // FR-047: "stops repeat notification, does not clear the underlying condition" — the
    // next AlertingWorker tick still evaluates the real condition and clears the alert
    // itself once it's actually healthy again. Also delivers the "acknowledged" webhook/
    // email event immediately, per contracts/alert-webhook.md's delivery semantics.
    [HttpPost("{id:guid}/acknowledge")]
    [RequireOperation(Operation.AcknowledgeAlerts)]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var alert = await _alertRepository.GetByIdAsync(id);
        if (alert is null)
        {
            return NotFound();
        }

        if (!alert.IsOpen)
        {
            return BadRequest("This alert has already cleared.");
        }

        var actorUserId = _actorContext.ActorUserId ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        var alertEvent = await _alertingService.AcknowledgeAsync(id, actorUserId, now);

        await _auditLogger.RecordAsync(
            "AcknowledgeAlert", "Alert", id.ToString(),
            before: new { acknowledged = false }, after: new { acknowledged = true, acknowledgedBy = actorUserId });

        await DeliverAsync(alertEvent, now);

        return Ok(ToDto(alertEvent.Alert));
    }

    private async Task DeliverAsync(AlertEvent alertEvent, DateTimeOffset occurredAt)
    {
        var destination = _notificationOptions.For(alertEvent.Alert.ConditionType.ToString());

        if (destination.EmailRecipients.Length > 0)
        {
            var outcome = await _emailSender.SendAsync(
                destination.EmailRecipients,
                $"[Attribution Platform] Alert acknowledged: {alertEvent.Alert.ConditionType}",
                $"Alert {alertEvent.Alert.Id} ({alertEvent.Alert.ConditionType}) was acknowledged.",
                HttpContext.RequestAborted);
            await _deliveryStatusRepository.RecordAttemptAsync(NotificationChannel.Email, outcome.Success, outcome.FailureReason, occurredAt);
        }

        if (!string.IsNullOrWhiteSpace(destination.WebhookUrl))
        {
            var payload = new AlertWebhookPayload(
                AlertId: alertEvent.Alert.Id.ToString(),
                ConditionType: alertEvent.Alert.ConditionType.ToString(),
                Scope: new Dictionary<string, string?> { ["scope_ref"] = alertEvent.Alert.ScopeRef },
                Status: "acknowledged",
                Threshold: alertEvent.Alert.Threshold,
                CurrentValue: alertEvent.CurrentValue,
                RaisedAt: alertEvent.Alert.RaisedAt,
                OccurredAt: occurredAt);
            var outcome = await _webhookSender.SendAsync(destination.WebhookUrl, payload, HttpContext.RequestAborted);
            await _deliveryStatusRepository.RecordAttemptAsync(NotificationChannel.Webhook, outcome.Success, outcome.FailureReason, occurredAt);
        }
    }

    private static object ToDto(Alert alert) => new
    {
        id = alert.Id,
        condition_type = alert.ConditionType.ToString(),
        scope_ref = alert.ScopeRef,
        threshold = alert.Threshold,
        raised_at = alert.RaisedAt,
        last_notified_at = alert.LastNotifiedAt,
        acknowledged_at = alert.AcknowledgedAt,
        acknowledged_by = alert.AcknowledgedBy,
        cleared_at = alert.ClearedAt,
    };
}
