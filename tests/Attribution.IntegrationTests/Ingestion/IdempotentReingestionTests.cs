using Attribution.Application.Attribution;
using Attribution.Application.Ingestion;
using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Ingestion;

// SC-002: re-ingesting an identical batch three times must produce zero change in any
// report total — here measured directly against the underlying Call/Attribution/Call Leg
// counts a report would reconcile against, since reporting itself (User Story 4) doesn't
// exist yet. Runs against the project's shared MySQL database (TestSupport.TestDatabase —
// the same database production uses), so assertions compare count *deltas* rather than
// absolute counts: the table is never empty at the start of a run the way a disposable
// per-test container's would be.
public class IdempotentReingestionTests : IAsyncLifetime
{
    // feed is varchar(32) — keep the unique suffix short.
    private readonly string _feed = $"test-{Guid.NewGuid():N}"[..32];
    private readonly string _did = $"+44163{Random.Shared.Next(1000000, 9999999)}";
    private readonly string _sourceRecordId = $"sc002-call-{Guid.NewGuid()}";
    private readonly string _sourceLegId = $"sc002-leg-{Guid.NewGuid()}";

    private IngestionService _ingestionService = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var callRepository = new CallRepository(connectionFactory);
        var callLegRepository = new CallLegRepository(connectionFactory);
        var checkpointRepository = new IngestionCheckpointRepository(connectionFactory);
        var attributionRepository = new AttributionRepository(connectionFactory);
        var attributionService = new AttributionService(
            new TrackingNumberRepository(connectionFactory), new AllocationRepository(connectionFactory),
            attributionRepository, new ReviewCaseRepository(connectionFactory));

        var qualificationResultRepository = new QualificationResultRepository(connectionFactory);
        var sessionRepository = new SessionRepository(connectionFactory);
        var publicationRepository = new ConversionPublicationRepository(connectionFactory);
        var publicationService = new PublicationService(publicationRepository, sessionRepository);
        var qualificationService = new QualificationService(
            new QualificationRuleRepository(connectionFactory), qualificationResultRepository,
            sessionRepository, new WebsiteRepository(connectionFactory), publicationService);
        var auditLogger = new AuditLogger(new AuditRepository(connectionFactory), new SystemActorContext());
        var correctionService = new CorrectionService(publicationRepository, sessionRepository, new NoOpGoogleAdsClient(), auditLogger);

        var reDerivationService = new ReDerivationService(
            callRepository, attributionRepository, qualificationResultRepository, attributionService, qualificationService, correctionService);
        _ingestionService = new IngestionService(
            callRepository, callLegRepository, checkpointRepository, attributionService, reDerivationService, qualificationService);

        await SeedAllocatedTrackingNumberAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReingestingAnIdenticalBatchThreeTimes_LeavesEveryUnderlyingCountUnchanged()
    {
        // Whole-second — the precision a real 8x8 CDR timestamp actually carries.
        // DateTimeOffset.UtcNow's genuine sub-microsecond jitter (real on Windows, via
        // GetSystemTimePreciseAsFileTime) doesn't survive a round trip through MySQL's
        // microsecond-precision DATETIME(6) columns, which would make that storage-precision
        // ceiling — not a real restatement — look like a change. No real CDR source jitters
        // at that resolution, so this reflects realistic source data rather than papering
        // over a production gap.
        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var page = new Analytics8x8Page(
            new[]
            {
                new Analytics8x8CallRecord(
                    _sourceRecordId, CallDirection.Inbound, _did, "+441632960999", startedAt,
                    startedAt.AddSeconds(2), startedAt.AddSeconds(90), 88, "answered", IsFinal: true),
            },
            new[] { new Analytics8x8CallLegRecord(_sourceRecordId, _sourceLegId, "primary", startedAt, startedAt.AddSeconds(90)) },
            NextCheckpointPosition: "pos-1");

        var (callsBefore, legsBefore, attributionsBefore) = await CountRowsAsync();

        await _ingestionService.ProcessPageAsync(_feed, page, DateTimeOffset.UtcNow);
        var (callsAfterFirst, legsAfterFirst, attributionsAfterFirst) = await CountRowsAsync();

        await _ingestionService.ProcessPageAsync(_feed, page, DateTimeOffset.UtcNow);
        await _ingestionService.ProcessPageAsync(_feed, page, DateTimeOffset.UtcNow);
        var (callsAfterThree, legsAfterThree, attributionsAfterThree) = await CountRowsAsync();

        Assert.Equal(callsBefore + 1, callsAfterFirst);
        Assert.Equal(legsBefore + 1, legsAfterFirst);
        Assert.Equal(attributionsBefore + 1, attributionsAfterFirst);
        Assert.Equal(callsAfterFirst, callsAfterThree);
        Assert.Equal(legsAfterFirst, legsAfterThree);
        Assert.Equal(attributionsAfterFirst, attributionsAfterThree);
    }

    // Scoped to this test's own source_record_id rather than a bare table-wide COUNT(*) —
    // the shared database has other tests' rows in it, potentially concurrently (xUnit
    // parallelizes across test classes by default), so a table-wide count would be racy.
    private async Task<(int Calls, int CallLegs, int Attributions)> CountRowsAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var calls = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM calls WHERE source_record_id = @Id", new { Id = _sourceRecordId });
        var legs = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM call_legs WHERE source_call_record_id = @Id", new { Id = _sourceRecordId });
        var attributions = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attributions WHERE call_id = (SELECT id FROM calls WHERE source_record_id = @Id)",
            new { Id = _sourceRecordId });
        return (calls, legs, attributions);
    }

    private async Task SeedAllocatedTrackingNumberAsync()
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
                (@Id, 'Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'Europe/London', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });

        var poolId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Test Pool', 'website', @WebsiteId, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
            new { Id = poolId.ToString(), WebsiteId = websiteId.ToString() });

        var trackingNumberId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at) VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())",
            new { Id = trackingNumberId.ToString(), PoolId = poolId.ToString(), Did = _did });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = visitorId.ToString(), WebsiteId = websiteId.ToString() });

        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions
                (id, visitor_id, website_id, consent_state, provenance, started_at, expires_at)
            VALUES
                (@Id, @VisitorId, @WebsiteId, 'Granted', 'Ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new { Id = sessionId.ToString(), VisitorId = visitorId.ToString(), WebsiteId = websiteId.ToString(), ExpiresAt = now.AddHours(1) });

        await connection.ExecuteAsync(
            """
            INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
            VALUES (@Id, @TrackingNumberId, @SessionId, @PoolId, @WindowStart, @WindowEnd, 0, UTC_TIMESTAMP())
            """,
            new
            {
                Id = Guid.NewGuid().ToString(),
                TrackingNumberId = trackingNumberId.ToString(),
                SessionId = sessionId.ToString(),
                PoolId = poolId.ToString(),
                WindowStart = now.AddMinutes(-30),
                WindowEnd = now.AddHours(2),
            });
    }
}
