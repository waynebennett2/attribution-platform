using Attribution.Application.Attribution;
using Attribution.Application.Ingestion;
using Attribution.Domain.Calls;
using Attribution.Infrastructure.Data;
using Attribution.Infrastructure.Data.Migrations;
using Dapper;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace Attribution.IntegrationTests.Ingestion;

// SC-002: re-ingesting an identical batch three times must produce zero change in any
// report total — here measured directly against the underlying Call/Attribution/Call Leg
// counts a report would reconcile against, since reporting itself (User Story 4) doesn't
// exist yet. Could not be executed in the sandboxed environment this was authored in
// (Docker daemon unreachable) — verified by inspection/compilation only; expected to run
// in CI/local dev.
public class IdempotentReingestionTests : IAsyncLifetime
{
    private const string Feed = "8x8-cdr";
    private const string Did = "+441632960050";

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0").Build();
    private IngestionService _ingestionService = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        await _mysql.StartAsync();
        _connectionString = _mysql.GetConnectionString();
        MigrationRunner.ApplyMigrations(_connectionString);

        var connectionFactory = new MySqlConnectionFactory(_connectionString);
        var callRepository = new CallRepository(connectionFactory);
        var callLegRepository = new CallLegRepository(connectionFactory);
        var checkpointRepository = new IngestionCheckpointRepository(connectionFactory);
        var attributionRepository = new AttributionRepository(connectionFactory);
        var attributionService = new AttributionService(
            new TrackingNumberRepository(connectionFactory), new AllocationRepository(connectionFactory),
            attributionRepository, new ReviewCaseRepository(connectionFactory));
        var reDerivationService = new ReDerivationService(callRepository, attributionRepository, attributionService);
        _ingestionService = new IngestionService(callRepository, callLegRepository, checkpointRepository, attributionService, reDerivationService);

        await SeedAllocatedTrackingNumberAsync();
    }

    public async Task DisposeAsync() => await _mysql.DisposeAsync();

    [Fact]
    public async Task ReingestingAnIdenticalBatchThreeTimes_LeavesEveryUnderlyingCountUnchanged()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var page = new Analytics8x8Page(
            new[]
            {
                new Analytics8x8CallRecord(
                    "sc002-call-1", CallDirection.Inbound, Did, "+441632960999", startedAt,
                    startedAt.AddSeconds(2), startedAt.AddSeconds(90), 88, "answered", IsFinal: true),
            },
            new[] { new Analytics8x8CallLegRecord("sc002-call-1", "leg-1", "primary", startedAt, startedAt.AddSeconds(90)) },
            NextCheckpointPosition: "pos-1");

        await _ingestionService.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);
        var (callsAfterFirst, legsAfterFirst, attributionsAfterFirst) = await CountRowsAsync();

        await _ingestionService.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);
        await _ingestionService.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);
        var (callsAfterThree, legsAfterThree, attributionsAfterThree) = await CountRowsAsync();

        Assert.Equal(1, callsAfterFirst);
        Assert.Equal(1, legsAfterFirst);
        Assert.Equal(1, attributionsAfterFirst);
        Assert.Equal(callsAfterFirst, callsAfterThree);
        Assert.Equal(legsAfterFirst, legsAfterThree);
        Assert.Equal(attributionsAfterFirst, attributionsAfterThree);
    }

    private async Task<(int Calls, int CallLegs, int Attributions)> CountRowsAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        var calls = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM calls");
        var legs = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM call_legs");
        var attributions = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM attributions");
        return (calls, legs, attributions);
    }

    private async Task SeedAllocatedTrackingNumberAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
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
            new { Id = trackingNumberId.ToString(), PoolId = poolId.ToString(), Did = Did });

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
                (@Id, @VisitorId, @WebsiteId, 'granted', 'ordinary', UTC_TIMESTAMP(), @ExpiresAt)
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
