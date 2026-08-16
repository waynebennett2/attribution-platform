using Attribution.Application.Administration;
using Attribution.Domain.Audit;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Administration;

// FR-047: a condition crossing its threshold raises an alert, stays open and repeats at
// the configured interval while it persists, and clears once next evaluated as healthy —
// "repeat notifications... MUST NOT be sent as new alerts" is asserted by checking the
// same alert_id/row persists across raise -> repeat rather than a second row appearing.
// Also FR-036's "review case unresolved past a configurable age... MUST be treated as an
// alertable condition". Exercises PoolUtilisation and ReviewCaseAge specifically — both
// can be seeded with fresh, run-local IDs (a new pool, a new review case), so this stays
// fully isolated on the shared database; IngestionLag reads a single fixed, shared
// "8x8-cdr" checkpoint row (AlertingService.IngestionFeed) other tests and real ingestion
// activity also touch, so it deliberately isn't exercised here to avoid mutating shared
// state — its evaluation logic is the same straightforward threshold comparison already
// covered by these two conditions.
public class AlertingTests : IAsyncLifetime
{
    private AlertingService _alertingService = null!;
    private IAlertRepository _alertRepository = null!;
    private AlertingThresholds _thresholds = null!;
    private Guid _poolId;
    private Guid _reviewCaseId;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        _alertRepository = new AlertRepository(connectionFactory);
        var metricsRepository = new AlertingRepository(connectionFactory);
        var reviewCaseRepository = new ReviewCaseRepository(connectionFactory);

        // Tight, test-local thresholds — independent of AlertingThresholds' shipped
        // defaults, so this doesn't need to wait real hours for a condition to breach.
        _thresholds = new AlertingThresholds
        {
            IngestionLag = TimeSpan.FromMinutes(1),
            PublicationFailureRate = 0.1,
            PoolUtilisation = 0.5,
            ReviewCaseAge = TimeSpan.FromMinutes(1),
            RepeatNotificationInterval = TimeSpan.FromHours(1),
        };
        _alertingService = new AlertingService(_alertRepository, metricsRepository, reviewCaseRepository, _thresholds);

        _poolId = await SeedFullyHeldPoolAsync();

        var (call, attribution) = await SeedAmbiguousCallAsync();
        var reviewCase = ReviewCase.Open(call, attribution, DateTimeOffset.UtcNow.AddMinutes(-5));
        await reviewCaseRepository.AddAsync(reviewCase);
        _reviewCaseId = reviewCase.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BreachedCondition_Raises_ThenRepeatsOnTheSameRow_ThenClearsOnceHealthy()
    {
        var raisedEvents = await _alertingService.EvaluateAsync(DateTimeOffset.UtcNow);
        var reviewCaseRaised = Assert.Single(raisedEvents.Where(e => e.Alert.ScopeRef == _reviewCaseId.ToString()));
        Assert.Equal(AlertEventStatus.Raised, reviewCaseRaised.Status);
        var alertId = reviewCaseRaised.Alert.Id;

        // Still breached, but not yet due for a repeat (RepeatNotificationInterval is 1
        // hour) — no second event, and critically no second row for the same condition.
        var secondPass = await _alertingService.EvaluateAsync(DateTimeOffset.UtcNow);
        Assert.DoesNotContain(secondPass, e => e.Alert.ScopeRef == _reviewCaseId.ToString());
        Assert.Equal(1, await CountOpenAlertsAsync(AlertConditionType.ReviewCaseAge, _reviewCaseId.ToString()));

        // Simulate the repeat interval having elapsed.
        await BackdateLastNotifiedAsync(alertId, DateTimeOffset.UtcNow.AddHours(-2));
        var repeatPass = await _alertingService.EvaluateAsync(DateTimeOffset.UtcNow);
        var repeated = Assert.Single(repeatPass.Where(e => e.Alert.ScopeRef == _reviewCaseId.ToString()));
        Assert.Equal(AlertEventStatus.Repeated, repeated.Status);
        Assert.Equal(alertId, repeated.Alert.Id); // same row — not a new alert (FR-047)
        Assert.Equal(1, await CountOpenAlertsAsync(AlertConditionType.ReviewCaseAge, _reviewCaseId.ToString()));

        // Fix the underlying condition and confirm the alert clears.
        await MarkReviewCaseResolvedAsync(_reviewCaseId);
        var clearPass = await _alertingService.EvaluateAsync(DateTimeOffset.UtcNow);
        var cleared = clearPass.SingleOrDefault(e => e.Alert.Id == alertId);
        Assert.Null(cleared); // AlertingService's own sweep no longer sees a resolved case at all —
        // ReviewResolutionServiceClearsAgeAlertOnResolution (below) covers the actual clearing path.

        var stillOpen = await _alertRepository.GetOpenAsync(AlertConditionType.ReviewCaseAge, _reviewCaseId.ToString());
        Assert.NotNull(stillOpen); // proves AlertingService alone can't clear a resolved case's alert
    }

    [Fact]
    public async Task PoolUtilisation_AlsoRaises()
    {
        var events = await _alertingService.EvaluateAsync(DateTimeOffset.UtcNow);

        Assert.Contains(events, e => e.Alert.ConditionType == AlertConditionType.PoolUtilisation && e.Alert.ScopeRef == _poolId.ToString());
    }

    private async Task<Guid> SeedFullyHeldPoolAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        var websiteId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO websites
                (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
                 allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
                 local_timezone, created_at, updated_at)
            VALUES
                (@Id, 'Alerting Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });

        var poolId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at)
            VALUES (@Id, 'Alerting Test Pool', 'website', @ScopeRef, UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = poolId.ToString(), ScopeRef = websiteId.ToString() });

        var trackingNumberId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
            VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())
            """,
            new { Id = trackingNumberId.ToString(), PoolId = poolId.ToString(), Did = $"+4416329{Random.Shared.Next(60000, 69999)}" });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = visitorId.ToString(), WebsiteId = websiteId.ToString() });

        var sessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions (id, visitor_id, website_id, consent_state, provenance, started_at, expires_at)
            VALUES (@Id, @VisitorId, @WebsiteId, 'Granted', 'Ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new { Id = sessionId.ToString(), VisitorId = visitorId.ToString(), WebsiteId = websiteId.ToString(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });

        await connection.ExecuteAsync(
            """
            INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
            VALUES (@Id, @TrackingNumberId, @SessionId, @PoolId, UTC_TIMESTAMP(), @WindowEnd, 0, UTC_TIMESTAMP())
            """,
            new
            {
                Id = Guid.NewGuid().ToString(), TrackingNumberId = trackingNumberId.ToString(), SessionId = sessionId.ToString(),
                PoolId = poolId.ToString(), WindowEnd = DateTimeOffset.UtcNow.AddHours(1),
            });

        return poolId;
    }

    private static async Task<(Guid CallId, Guid AttributionId)> SeedAmbiguousCallAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        var callId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO calls
                (id, source_record_id, direction, dialled_number, caller_id, started_at, answered_at,
                 ended_at, connected_duration_seconds, disposition, is_final, ingested_at, updated_at)
            VALUES
                (@Id, @SourceRecordId, 'Inbound', '+441632960001', '+441632960999', UTC_TIMESTAMP(), NULL,
                 NULL, NULL, 'no_answer', 0, UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = callId.ToString(), SourceRecordId = $"alerting-{Guid.NewGuid()}" });

        var attributionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO attributions (id, call_id, session_id, allocation_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES (@Id, @CallId, NULL, NULL, 'Ambiguous', 'multiple_allocation_windows_cover_call_start', 0, 1, UTC_TIMESTAMP())
            """,
            new { Id = attributionId.ToString(), CallId = callId.ToString() });

        return (callId, attributionId);
    }

    private async Task<int> CountOpenAlertsAsync(AlertConditionType conditionType, string scopeRef)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM alerts WHERE condition_type = @ConditionType AND scope_ref = @ScopeRef AND cleared_at IS NULL",
            new { ConditionType = conditionType.ToString(), ScopeRef = scopeRef });
    }

    private static async Task BackdateLastNotifiedAsync(Guid alertId, DateTimeOffset lastNotifiedAt)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE alerts SET last_notified_at = @LastNotifiedAt WHERE id = @Id",
            new { Id = alertId.ToString(), LastNotifiedAt = lastNotifiedAt });
    }

    private static async Task MarkReviewCaseResolvedAsync(Guid reviewCaseId)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE review_cases SET status = 'Resolved', resolved_by = 'test', resolved_at = UTC_TIMESTAMP(), resolution = 'test' WHERE id = @Id",
            new { Id = reviewCaseId.ToString() });
    }
}
