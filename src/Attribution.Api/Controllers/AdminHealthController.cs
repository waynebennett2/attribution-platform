using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Domain.Publication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Integration health. FR-034. Reads the same
// IAlertingMetricsRepository AlertingService (T092) evaluates against, so what an
// administrator sees here can never disagree with what triggered (or didn't trigger) an
// alert — one source of truth for "is this healthy", not two independently-derived views.
[ApiController]
[Route("v1/admin/health")]
[Authorize]
public sealed class AdminHealthController : ControllerBase
{
    // Must match AlertingService.IngestionFeed / IngestionWorker.Feed.
    private const string IngestionFeed = "8x8-cdr";

    private readonly IAlertingMetricsRepository _metricsRepository;
    private readonly INotificationDeliveryStatusRepository _deliveryStatusRepository;
    private readonly AlertingThresholds _thresholds;

    public AdminHealthController(
        IAlertingMetricsRepository metricsRepository,
        INotificationDeliveryStatusRepository deliveryStatusRepository,
        AlertingThresholds thresholds)
    {
        _metricsRepository = metricsRepository;
        _deliveryStatusRepository = deliveryStatusRepository;
        _thresholds = thresholds;
    }

    [HttpGet("ingestion")]
    [RequireOperation(Operation.ViewIntegrationHealth)]
    public async Task<IActionResult> Ingestion()
    {
        var lastUpdate = await _metricsRepository.GetLastIngestionCheckpointUpdateAsync(IngestionFeed);
        var lag = lastUpdate is null ? (TimeSpan?)null : DateTimeOffset.UtcNow - lastUpdate.Value;
        return Ok(new
        {
            feed = IngestionFeed,
            last_successful_ingestion_at = lastUpdate,
            current_lag_seconds = lag?.TotalSeconds,
            healthy = lag is not null && lag < _thresholds.IngestionLag,
        });
    }

    [HttpGet("publication")]
    [RequireOperation(Operation.ViewIntegrationHealth)]
    public async Task<IActionResult> Publication()
    {
        var results = new List<object>();
        foreach (var destination in Enum.GetValues<PublicationDestination>())
        {
            var (sent, failed) = await _metricsRepository.GetRecentPublicationOutcomeCountsAsync(destination, TimeSpan.FromHours(24));
            var total = sent + failed;
            results.Add(new
            {
                destination = destination.ToString(),
                sent,
                failed,
                failure_rate = total == 0 ? (double?)null : (double)failed / total,
                healthy = total == 0 || (double)failed / total <= _thresholds.PublicationFailureRate,
            });
        }

        return Ok(results);
    }

    [HttpGet("pools")]
    [RequireOperation(Operation.ViewIntegrationHealth)]
    public async Task<IActionResult> Pools()
    {
        var pools = await _metricsRepository.GetPoolUtilisationAsync();
        return Ok(pools.Select(p => new
        {
            pool_id = p.PoolId,
            pool_name = p.PoolName,
            held_numbers = p.HeldNumbers,
            total_numbers = p.TotalNumbers,
            utilisation = p.TotalNumbers == 0 ? (double?)null : (double)p.HeldNumbers / p.TotalNumbers,
            warning_threshold = _thresholds.PoolUtilisation,
            exhaustion_warning = p.TotalNumbers > 0 && (double)p.HeldNumbers / p.TotalNumbers >= _thresholds.PoolUtilisation,
        }));
    }

    // FR-047: "failure to deliver a notification MUST NOT suppress the underlying
    // condition on the integration health view of FR-034" implies the reverse too — a
    // stuck delivery pipeline should itself be visible here, distinct from whether the
    // conditions it would have notified about are healthy.
    [HttpGet("notifications")]
    [RequireOperation(Operation.ViewIntegrationHealth)]
    public async Task<IActionResult> Notifications()
    {
        var statuses = await _deliveryStatusRepository.GetAllAsync();
        return Ok(statuses.Select(s => new
        {
            channel = s.Channel.ToString(),
            last_attempt_at = s.LastAttemptAt,
            last_success_at = s.LastSuccessAt,
            last_failure_at = s.LastFailureAt,
            last_failure_reason = s.LastFailureReason,
            healthy = s.LastFailureAt is null || (s.LastSuccessAt is not null && s.LastSuccessAt >= s.LastFailureAt),
        }));
    }
}
