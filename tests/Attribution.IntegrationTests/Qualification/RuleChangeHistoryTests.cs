using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using MySqlConnector;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;

namespace Attribution.IntegrationTests.Qualification;

// SC-011: publishing a new qualification rule version must never alter the result or
// rule-version reference of a call already judged under the prior version — only calls
// decided from that point forward see the new version. Runs against the project's shared
// MySQL database (TestSupport.TestDatabase).
public class RuleChangeHistoryTests : IAsyncLifetime
{
    // A random per-run campaign scope keeps this test's rule versions isolated from every
    // other test/run sharing this database.
    private readonly string _campaign = $"test-campaign-{Guid.NewGuid():N}";

    private RuleVersioningService _versioningService = null!;
    private QualificationService _qualificationService = null!;
    private IQualificationResultRepository _resultRepository = null!;
    private ICallRepository _callRepository = null!;
    private IAttributionRepository _attributionRepository = null!;
    private Guid _sessionId;
    private Guid _allocationId;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var ruleRepository = new QualificationRuleRepository(connectionFactory);
        _resultRepository = new QualificationResultRepository(connectionFactory);
        var sessionRepository = new SessionRepository(connectionFactory);
        var websiteRepository = new WebsiteRepository(connectionFactory);
        _callRepository = new CallRepository(connectionFactory);
        _attributionRepository = new AttributionRepository(connectionFactory);

        _versioningService = new RuleVersioningService(ruleRepository);
        _qualificationService = new QualificationService(ruleRepository, _resultRepository, sessionRepository, websiteRepository);

        (_, _sessionId, _allocationId) = await SeedWebsiteSessionAndAllocationAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PublishingANewVersion_LeavesAPreviouslyJudgedCallsResultAndRuleReference_Unchanged()
    {
        var v1Start = DateTimeOffset.UtcNow.AddDays(-1);
        var v1 = await _versioningService.CreateVersionAsync(
            QualificationScopeType.Campaign, _campaign, new QualificationConditions(null, true, null, null),
            v1Start, "test-admin", v1Start);

        var earlierCall = Call.Create(
            $"rch-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", "+441632960999",
            v1Start.AddHours(1), answeredAt: v1Start.AddHours(1), endedAt: v1Start.AddHours(1).AddSeconds(30),
            connectedDurationSeconds: 30, disposition: "answered", isFinal: true, DateTimeOffset.UtcNow);
        await _callRepository.AddAsync(earlierCall);
        var earlierAttribution = DomainAttribution.Attributed(earlierCall.Id, _sessionId, _allocationId, earlierCall.StartedAt);
        await _attributionRepository.AddAsync(earlierAttribution);

        var earlierResult = await _qualificationService.QualifyAsync(earlierCall, earlierAttribution, DateTimeOffset.UtcNow);
        Assert.True(earlierResult.IsQualified);
        Assert.Equal(v1.Id, earlierResult.QualificationRuleId);

        // Publish a new, stricter version.
        var v2Start = DateTimeOffset.UtcNow;
        var v2 = await _versioningService.CreateVersionAsync(
            QualificationScopeType.Campaign, _campaign, new QualificationConditions(null, true, 9999, null),
            v2Start, "test-admin", v2Start);

        // The earlier call's already-decided result must be untouched by the new version.
        var reFetchedEarlierResult = await _resultRepository.GetCurrentByCallIdAsync(earlierCall.Id);
        Assert.NotNull(reFetchedEarlierResult);
        Assert.True(reFetchedEarlierResult!.IsCurrent);
        Assert.True(reFetchedEarlierResult.IsQualified);
        Assert.Equal(v1.Id, reFetchedEarlierResult.QualificationRuleId);

        // A new call, decided after v2's effective_start, uses v2 — and v2's stricter
        // 9999-second minimum means it does not qualify.
        var laterCall = Call.Create(
            $"rch-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", "+441632960999",
            v2Start.AddMinutes(1), answeredAt: v2Start.AddMinutes(1), endedAt: v2Start.AddMinutes(1).AddSeconds(30),
            connectedDurationSeconds: 30, disposition: "answered", isFinal: true, DateTimeOffset.UtcNow);
        await _callRepository.AddAsync(laterCall);
        var laterAttribution = DomainAttribution.Attributed(laterCall.Id, _sessionId, _allocationId, laterCall.StartedAt);
        await _attributionRepository.AddAsync(laterAttribution);

        var laterResult = await _qualificationService.QualifyAsync(laterCall, laterAttribution, DateTimeOffset.UtcNow);

        Assert.Equal(v2.Id, laterResult.QualificationRuleId);
        Assert.False(laterResult.IsQualified);
    }

    private async Task<(Guid WebsiteId, Guid SessionId, Guid AllocationId)> SeedWebsiteSessionAndAllocationAsync()
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
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });

        var poolId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Test Pool', 'website', @WebsiteId, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
            new { Id = poolId.ToString(), WebsiteId = websiteId.ToString() });

        var trackingNumberId = Guid.NewGuid();
        var did = $"+4416329{Random.Shared.Next(40000, 49999)}";
        await connection.ExecuteAsync(
            "INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at) VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())",
            new { Id = trackingNumberId.ToString(), PoolId = poolId.ToString(), Did = did });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = visitorId.ToString(), WebsiteId = websiteId.ToString() });

        var sessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions
                (id, visitor_id, website_id, utm_campaign, consent_state, provenance, started_at, expires_at)
            VALUES
                (@Id, @VisitorId, @WebsiteId, @Campaign, 'Granted', 'Ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new
            {
                Id = sessionId.ToString(),
                VisitorId = visitorId.ToString(),
                WebsiteId = websiteId.ToString(),
                Campaign = _campaign,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            });

        // The allocation's own window is irrelevant to these tests — QualificationService
        // never consults it, only AttributionService does — this row exists purely to
        // satisfy attributions.allocation_id's foreign key.
        var allocationId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
            VALUES (@Id, @TrackingNumberId, @SessionId, @PoolId, @WindowStart, @WindowEnd, 0, UTC_TIMESTAMP())
            """,
            new
            {
                Id = allocationId.ToString(),
                TrackingNumberId = trackingNumberId.ToString(),
                SessionId = sessionId.ToString(),
                PoolId = poolId.ToString(),
                WindowStart = DateTimeOffset.UtcNow.AddDays(-2),
                WindowEnd = DateTimeOffset.UtcNow.AddDays(2),
            });

        return (websiteId, sessionId, allocationId);
    }
}
