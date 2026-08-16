using Attribution.Domain.Audit;
using Attribution.Domain.Publication;

namespace Attribution.Application.Administration;

public enum AlertEventStatus
{
    Raised,
    Repeated,
    Acknowledged,
    Cleared,
}

// AlertingWorker.cs (T093) POSTs one of these per contracts/alert-webhook.md and emails the
// configured recipients; CurrentValue/Threshold are already human-readable so delivery
// needs no further formatting knowledge of what each condition type means.
public sealed record AlertEvent(Alert Alert, AlertEventStatus Status, string CurrentValue);

// FR-047: evaluates each condition type against its configured threshold and raises,
// repeats or clears the single open Alert row for that (condition, scope) — never a second
// row while one is already open (data-model.md's invariant). Called once per tick from
// AlertingWorker (T093); pure evaluate-and-persist, no notification delivery here so the
// worker can dispatch its emails/webhooks from a plain list of events without re-deriving
// what changed.
public sealed class AlertingService
{
    // Must match IngestionWorker.Feed (Attribution.Workers can't be referenced from here —
    // it depends on this project, not the other way round).
    private const string IngestionFeed = "8x8-cdr";

    private readonly IAlertRepository _alertRepository;
    private readonly IAlertingMetricsRepository _metricsRepository;
    private readonly IReviewCaseRepository _reviewCaseRepository;
    private readonly AlertingThresholds _thresholds;

    public AlertingService(
        IAlertRepository alertRepository,
        IAlertingMetricsRepository metricsRepository,
        IReviewCaseRepository reviewCaseRepository,
        AlertingThresholds thresholds)
    {
        _alertRepository = alertRepository;
        _metricsRepository = metricsRepository;
        _reviewCaseRepository = reviewCaseRepository;
        _thresholds = thresholds;
    }

    public async Task<IReadOnlyList<AlertEvent>> EvaluateAsync(DateTimeOffset now)
    {
        var events = new List<AlertEvent>();

        events.AddRange(await EvaluateIngestionLagAsync(now));
        events.AddRange(await EvaluatePublicationFailureRateAsync(now));
        events.AddRange(await EvaluatePoolUtilisationAsync(now));
        events.AddRange(await EvaluateReviewCaseAgeAsync(now));

        // AllocationFailureRate (FR-047's fifth condition) is deliberately not evaluated:
        // no allocation-attempt log exists anywhere in the schema, only successful
        // Allocation rows, so there is no failure signal to read. A documented
        // simplification for this increment rather than a fabricated metric — see
        // AllocationService's own "Simplification for this increment" convention.

        return events;
    }

    public async Task<AlertEvent> AcknowledgeAsync(Guid alertId, string acknowledgedBy, DateTimeOffset now)
    {
        var alert = await _alertRepository.GetByIdAsync(alertId)
            ?? throw new InvalidOperationException($"Unknown alert {alertId}.");

        alert.Acknowledge(acknowledgedBy, now);
        await _alertRepository.UpdateAsync(alert);

        return new AlertEvent(alert, AlertEventStatus.Acknowledged, alert.Threshold);
    }

    private async Task<IReadOnlyList<AlertEvent>> EvaluateIngestionLagAsync(DateTimeOffset now)
    {
        var lastUpdate = await _metricsRepository.GetLastIngestionCheckpointUpdateAsync(IngestionFeed);
        var lag = lastUpdate is null ? TimeSpan.MaxValue : now - lastUpdate.Value;
        var breached = lag > _thresholds.IngestionLag;

        var evt = await EvaluateConditionAsync(
            AlertConditionType.IngestionLag, IngestionFeed, breached,
            $"lag > {_thresholds.IngestionLag}", lastUpdate is null ? "never ingested" : $"lag {now - lastUpdate.Value}", now);
        return evt is null ? [] : [evt];
    }

    private async Task<IReadOnlyList<AlertEvent>> EvaluatePublicationFailureRateAsync(DateTimeOffset now)
    {
        var events = new List<AlertEvent>();
        foreach (var destination in Enum.GetValues<PublicationDestination>())
        {
            var (sent, failed) = await _metricsRepository.GetRecentPublicationOutcomeCountsAsync(destination, TimeSpan.FromHours(24));
            var total = sent + failed;
            var breached = total > 0 && (double)failed / total > _thresholds.PublicationFailureRate;

            var evt = await EvaluateConditionAsync(
                AlertConditionType.PublicationFailureRate, destination.ToString(), breached,
                $"failure rate > {_thresholds.PublicationFailureRate:P0}",
                total == 0 ? "no publications yet" : $"{failed}/{total} failed", now);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    private async Task<IReadOnlyList<AlertEvent>> EvaluatePoolUtilisationAsync(DateTimeOffset now)
    {
        var events = new List<AlertEvent>();
        var pools = await _metricsRepository.GetPoolUtilisationAsync();
        foreach (var pool in pools)
        {
            var breached = pool.TotalNumbers > 0 && (double)pool.HeldNumbers / pool.TotalNumbers >= _thresholds.PoolUtilisation;

            var evt = await EvaluateConditionAsync(
                AlertConditionType.PoolUtilisation, pool.PoolId.ToString(), breached,
                $"utilisation >= {_thresholds.PoolUtilisation:P0}",
                pool.TotalNumbers == 0 ? "pool is empty" : $"{pool.HeldNumbers}/{pool.TotalNumbers} held", now);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    private async Task<IReadOnlyList<AlertEvent>> EvaluateReviewCaseAgeAsync(DateTimeOffset now)
    {
        var events = new List<AlertEvent>();
        var openCases = await _reviewCaseRepository.GetOpenAsync();
        foreach (var reviewCase in openCases)
        {
            var age = now - reviewCase.OpenedAt;
            var breached = age >= _thresholds.ReviewCaseAge;

            var evt = await EvaluateConditionAsync(
                AlertConditionType.ReviewCaseAge, reviewCase.Id.ToString(), breached,
                $"age >= {_thresholds.ReviewCaseAge}", $"open for {age}", now);
            if (evt is { Status: AlertEventStatus.Raised })
            {
                reviewCase.MarkAgeAlertRaised(now);
                await _reviewCaseRepository.UpdateAsync(reviewCase);
            }

            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    // The single raise/repeat/clear state machine every condition type shares, keyed by
    // (conditionType, scopeRef) per data-model.md's at-most-one-open-row invariant.
    private async Task<AlertEvent?> EvaluateConditionAsync(
        AlertConditionType conditionType, string? scopeRef, bool breached, string thresholdDescription, string currentValueDescription, DateTimeOffset now)
    {
        var existing = await _alertRepository.GetOpenAsync(conditionType, scopeRef);

        if (breached)
        {
            if (existing is null)
            {
                var alert = Alert.Raise(conditionType, scopeRef, thresholdDescription, now);
                await _alertRepository.AddAsync(alert);
                return new AlertEvent(alert, AlertEventStatus.Raised, currentValueDescription);
            }

            if (now - existing.LastNotifiedAt >= _thresholds.RepeatNotificationInterval)
            {
                existing.RecordRepeatNotification(now);
                await _alertRepository.UpdateAsync(existing);
                return new AlertEvent(existing, AlertEventStatus.Repeated, currentValueDescription);
            }

            return null;
        }

        if (existing is not null)
        {
            existing.Clear(now);
            await _alertRepository.UpdateAsync(existing);
            return new AlertEvent(existing, AlertEventStatus.Cleared, currentValueDescription);
        }

        return null;
    }
}
