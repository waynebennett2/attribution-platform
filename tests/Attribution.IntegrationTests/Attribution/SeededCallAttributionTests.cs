using Attribution.Application.Attribution;
using Attribution.Domain.Calls;
using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Infrastructure.Data;
using Attribution.Infrastructure.Data.Migrations;
using Dapper;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace Attribution.IntegrationTests.Attribution;

// SC-001: exercises the full seeded-call scenario set against a real MySQL 8.0+ instance
// (Testcontainers) — every outcome the acceptance test requires must land in the expected
// state with retrievable evidence. Could not be executed in the sandboxed environment this
// was authored in (Docker daemon unreachable) — verified by inspection/compilation only;
// expected to run in CI/local dev.
public class SeededCallAttributionTests : IAsyncLifetime
{
    private static readonly TimeSpan Extension = TimeSpan.FromMinutes(30);

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0").Build();
    private AttributionService _attributionService = null!;
    private ICallRepository _callRepository = null!;
    private IAttributionRepository _attributionRepository = null!;
    private ITrackingNumberRepository _trackingNumberRepository = null!;
    private IAllocationRepository _allocationRepository = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        await _mysql.StartAsync();
        MigrationRunner.ApplyMigrations(_mysql.GetConnectionString());

        var connectionFactory = new MySqlConnectionFactory(_mysql.GetConnectionString());
        _callRepository = new CallRepository(connectionFactory);
        _attributionRepository = new AttributionRepository(connectionFactory);
        _trackingNumberRepository = new TrackingNumberRepository(connectionFactory);
        _allocationRepository = new AllocationRepository(connectionFactory);
        _attributionService = new AttributionService(
            _trackingNumberRepository, _allocationRepository, _attributionRepository, new ReviewCaseRepository(connectionFactory));
    }

    public async Task DisposeAsync() => await _mysql.DisposeAsync();

    [Fact]
    public async Task CallInsideTheAllocationWindow_IsAttributed()
    {
        var callStart = DateTimeOffset.UtcNow;
        var (did, sessionId) = await SeedAllocatedNumberAsync(windowStart: callStart.AddMinutes(-10), sessionExpiresAt: callStart.AddMinutes(5));

        var attribution = await AttributeCallAsync("sc001-in-window", did, callStart);

        Assert.Equal(AttributionState.Attributed, attribution.State);
        Assert.Equal(sessionId, attribution.SessionId);
    }

    [Fact]
    public async Task CallAfterSessionExpiry_ButStillInsideTheFr018Extension_IsAttributed()
    {
        var sessionExpiresAt = DateTimeOffset.UtcNow;
        // Session ended 10 minutes ago; the call arrives 20 minutes into the 30-minute extension.
        var callStart = sessionExpiresAt.AddMinutes(20);
        var (did, sessionId) = await SeedAllocatedNumberAsync(windowStart: sessionExpiresAt.AddMinutes(-30), sessionExpiresAt);

        var attribution = await AttributeCallAsync("sc001-in-extension", did, callStart);

        Assert.Equal(AttributionState.Attributed, attribution.State);
        Assert.Equal(sessionId, attribution.SessionId);
    }

    [Fact]
    public async Task CallAfterTheAllocationWindowCloses_IsUnattributed()
    {
        var sessionExpiresAt = DateTimeOffset.UtcNow;
        // 30-minute extension plus one more minute — past window_end.
        var callStart = sessionExpiresAt.Add(Extension).AddMinutes(1);
        var (did, _) = await SeedAllocatedNumberAsync(windowStart: sessionExpiresAt.AddMinutes(-30), sessionExpiresAt);

        var attribution = await AttributeCallAsync("sc001-closed-window", did, callStart);

        Assert.Equal(AttributionState.Unattributed, attribution.State);
        Assert.Equal("no_allocation_window_covers_call_start", attribution.Reason);
        Assert.Null(attribution.SessionId);
    }

    [Fact]
    public async Task CallToANumberNeverAllocated_IsUnattributed()
    {
        var attribution = await AttributeCallAsync("sc001-never-allocated", "+441632960099", DateTimeOffset.UtcNow);

        Assert.Equal(AttributionState.Unattributed, attribution.State);
        Assert.Equal("number_never_allocated", attribution.Reason);
    }

    [Fact]
    public async Task CallToASuspendedNumber_StillAttributes_IfItsAllocationWindowStillCoversTheCall()
    {
        // FR-005: suspending a number never ends an allocation already in progress on it.
        var callStart = DateTimeOffset.UtcNow;
        var (did, sessionId) = await SeedAllocatedNumberAsync(windowStart: callStart.AddMinutes(-10), sessionExpiresAt: callStart.AddMinutes(5));
        var trackingNumber = await _trackingNumberRepository.GetByDidAsync(did);
        trackingNumber!.Suspend();
        await _trackingNumberRepository.UpdateAsync(trackingNumber);

        var attribution = await AttributeCallAsync("sc001-suspended-number", did, callStart);

        Assert.Equal(AttributionState.Attributed, attribution.State);
        Assert.Equal(sessionId, attribution.SessionId);
    }

    [Fact]
    public async Task CallDuringADaylightSavingTransition_IsAttributed_UsingUtcInstantsThroughout()
    {
        // UK clocks went forward at 01:00 UTC on 2024-03-31. Matching is pure DateTimeOffset
        // (UTC-anchored) comparison, so the transition itself must not affect the outcome.
        var transitionInstant = new DateTimeOffset(2024, 3, 31, 1, 0, 0, TimeSpan.Zero);
        var callStart = transitionInstant.AddMinutes(5);
        var (did, sessionId) = await SeedAllocatedNumberAsync(
            windowStart: transitionInstant.AddMinutes(-10), sessionExpiresAt: transitionInstant.AddMinutes(20));

        var attribution = await AttributeCallAsync("sc001-dst-transition", did, callStart);

        Assert.Equal(AttributionState.Attributed, attribution.State);
        Assert.Equal(sessionId, attribution.SessionId);
    }

    [Fact]
    public async Task CallPlacedAcrossMidnight_IsAttributed_UsingUtcInstantsThroughout()
    {
        var midnight = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var callStart = midnight.AddMinutes(2);
        var (did, sessionId) = await SeedAllocatedNumberAsync(
            windowStart: midnight.AddMinutes(-15), sessionExpiresAt: midnight.AddMinutes(20));

        var attribution = await AttributeCallAsync("sc001-midnight-crossing", did, callStart);

        Assert.Equal(AttributionState.Attributed, attribution.State);
        Assert.Equal(sessionId, attribution.SessionId);
    }

    private async Task<Domain.Calls.Attribution> AttributeCallAsync(string sourceRecordId, string did, DateTimeOffset startedAt)
    {
        var call = Call.Create(
            sourceRecordId, CallDirection.Inbound, did, callerId: "+441632960999",
            startedAt, answeredAt: null, endedAt: null, connectedDurationSeconds: null,
            disposition: null, isFinal: false, ingestedAt: DateTimeOffset.UtcNow);
        await _callRepository.AddAsync(call);

        var attribution = await _attributionService.AttributeAsync(call, DateTimeOffset.UtcNow);

        // FR-019: re-fetch from the database — proves the evidence is actually retrievable,
        // not just present on the in-memory object AttributeAsync returned.
        var stored = await _attributionRepository.GetCurrentByCallIdAsync(call.Id);
        Assert.NotNull(stored);
        Assert.Equal(attribution.State, stored!.State);
        return stored;
    }

    private async Task<(string Did, Guid SessionId)> SeedAllocatedNumberAsync(DateTimeOffset windowStart, DateTimeOffset sessionExpiresAt)
    {
        var did = $"+4416329{Random.Shared.Next(60000, 69999)}";
        var websiteId = await SeedWebsiteAsync();
        var poolId = await SeedPoolAsync();
        await SeedTrackingNumberAsync(poolId, did);
        var visitorId = await SeedVisitorAsync(websiteId);
        var sessionId = await SeedSessionAsync(visitorId, websiteId, sessionExpiresAt);

        var trackingNumber = await _trackingNumberRepository.GetByDidAsync(did);
        var allocation = Domain.Sessions.Allocation.Create(
            trackingNumber!.Id, sessionId, poolId, windowStart, sessionExpiresAt, Extension);
        await _allocationRepository.AddAsync(allocation);

        return (did, sessionId);
    }

    private async Task<Guid> SeedWebsiteAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
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
            new { Id = id.ToString() });
        return id;
    }

    private async Task<Guid> SeedPoolAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Test Pool', 'website', @Id, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
            new { Id = id.ToString() });
        return id;
    }

    private async Task SeedTrackingNumberAsync(Guid poolId, string did)
    {
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at) VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())",
            new { Id = Guid.NewGuid().ToString(), PoolId = poolId.ToString(), Did = did });
    }

    private async Task<Guid> SeedVisitorAsync(Guid websiteId)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = id.ToString(), WebsiteId = websiteId.ToString() });
        return id;
    }

    private async Task<Guid> SeedSessionAsync(Guid visitorId, Guid websiteId, DateTimeOffset expiresAt)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions
                (id, visitor_id, website_id, consent_state, provenance, started_at, expires_at)
            VALUES
                (@Id, @VisitorId, @WebsiteId, 'granted', 'ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new { Id = id.ToString(), VisitorId = visitorId.ToString(), WebsiteId = websiteId.ToString(), ExpiresAt = expiresAt });
        return id;
    }
}
