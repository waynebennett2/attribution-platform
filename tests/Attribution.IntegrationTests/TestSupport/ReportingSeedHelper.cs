using Attribution.Domain.Calls;
using Dapper;
using MySqlConnector;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.IntegrationTests.TestSupport;

// Shared seed-data builder for the User Story 4 reporting tests. Each instance owns one
// website/session/allocation "context" (random ids, one shared campaign), against which
// individual calls with known attribution/qualification outcomes are seeded directly via
// SQL — bypassing AttributionService/QualificationService entirely, since these tests
// exercise reporting (reading already-decided data), not the decision pipeline itself
// (already covered by the User Story 2/3 test suites).
public sealed class ReportingSeedHelper
{
    public Guid WebsiteId { get; private set; }
    public Guid SessionId { get; private set; }
    public string? Campaign { get; private set; }
    public string Did { get; private set; } = string.Empty;
    private Guid _allocationId;

    public static async Task<ReportingSeedHelper> CreateAsync(string? campaign = null)
    {
        var helper = new ReportingSeedHelper { Campaign = campaign };
        await helper.SeedContextAsync();
        return helper;
    }

    private async Task SeedContextAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        WebsiteId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO websites
                (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
                 allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
                 local_timezone, created_at, updated_at)
            VALUES
                (@Id, 'Reporting Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = WebsiteId.ToString() });

        var poolId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Reporting Test Pool', 'website', @WebsiteId, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
            new { Id = poolId.ToString(), WebsiteId = WebsiteId.ToString() });

        var trackingNumberId = Guid.NewGuid();
        Did = $"+4416329{Random.Shared.Next(10000, 19999)}";
        await connection.ExecuteAsync(
            "INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at) VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())",
            new { Id = trackingNumberId.ToString(), PoolId = poolId.ToString(), Did });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = visitorId.ToString(), WebsiteId = WebsiteId.ToString() });

        SessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions (id, visitor_id, website_id, utm_campaign, consent_state, provenance, started_at, expires_at)
            VALUES (@Id, @VisitorId, @WebsiteId, @Campaign, 'Granted', 'Ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new
            {
                Id = SessionId.ToString(), VisitorId = visitorId.ToString(), WebsiteId = WebsiteId.ToString(),
                Campaign, ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
            });

        _allocationId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
            VALUES (@Id, @TrackingNumberId, @SessionId, @PoolId, @WindowStart, @WindowEnd, 0, UTC_TIMESTAMP())
            """,
            new
            {
                Id = _allocationId.ToString(), TrackingNumberId = trackingNumberId.ToString(), SessionId = SessionId.ToString(),
                PoolId = poolId.ToString(), WindowStart = DateTimeOffset.UtcNow.AddYears(-1), WindowEnd = DateTimeOffset.UtcNow.AddYears(1),
            });
    }

    // isQualified: null = don't create a qualification_results row at all (e.g. an
    // ordinary "never derived" scenario isn't relevant here); true/false = a current
    // result exists with that outcome, matching what production always records for every
    // attributed call regardless of whether it happens to qualify.
    public async Task<Guid> SeedAttributedCallAsync(
        DateTimeOffset startedAt, CallDirection direction = CallDirection.Inbound, DateTimeOffset? answeredAt = null,
        int? connectedDurationSeconds = null, bool? isQualified = null)
    {
        var call = Call.Create(
            $"rpt-{Guid.NewGuid()}", direction, Did, "+441632960999", startedAt, answeredAt,
            answeredAt is null ? null : startedAt.AddSeconds(connectedDurationSeconds ?? 0),
            connectedDurationSeconds, answeredAt is null ? null : "answered", true, DateTimeOffset.UtcNow);

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertCallAsync(connection, call);

        var attribution = DomainAttribution.Attributed(call.Id, SessionId, _allocationId, startedAt);
        await InsertAttributionAsync(connection, attribution);

        if (isQualified is not null)
        {
            var ruleId = await GetDefaultRuleIdAsync(connection);
            await connection.ExecuteAsync(
                """
                INSERT INTO qualification_results (id, call_id, attribution_id, qualification_rule_id, is_qualified, is_current, decided_at)
                VALUES (@Id, @CallId, @AttributionId, @RuleId, @IsQualified, 1, UTC_TIMESTAMP())
                """,
                new
                {
                    Id = Guid.NewGuid().ToString(), CallId = call.Id.ToString(), AttributionId = attribution.Id.ToString(),
                    RuleId = ruleId.ToString(), IsQualified = isQualified.Value,
                });
        }

        return call.Id;
    }

    public async Task<Guid> SeedUnattributedCallAsync(DateTimeOffset startedAt, string reason)
    {
        var call = Call.Create(
            $"rpt-{Guid.NewGuid()}", CallDirection.Inbound, Did, "+441632960999", startedAt, startedAt,
            startedAt.AddSeconds(10), 10, "answered", true, DateTimeOffset.UtcNow);
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertCallAsync(connection, call);
        await InsertAttributionAsync(connection, DomainAttribution.Unattributed(call.Id, reason, startedAt));
        return call.Id;
    }

    public async Task<Guid> SeedAmbiguousCallAsync(DateTimeOffset startedAt)
    {
        var call = Call.Create(
            $"rpt-{Guid.NewGuid()}", CallDirection.Inbound, Did, "+441632960999", startedAt, startedAt,
            startedAt.AddSeconds(10), 10, "answered", true, DateTimeOffset.UtcNow);
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertCallAsync(connection, call);
        await InsertAttributionAsync(connection, DomainAttribution.Ambiguous(call.Id, "multiple_allocation_windows_cover_call_start", startedAt));
        return call.Id;
    }

    private static async Task InsertCallAsync(MySqlConnection connection, Call call) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO calls
                (id, source_record_id, direction, dialled_number, caller_id, started_at, answered_at,
                 ended_at, connected_duration_seconds, disposition, is_final, ingested_at, updated_at)
            VALUES
                (@Id, @SourceRecordId, @Direction, @DialledNumber, @CallerId, @StartedAt, @AnsweredAt,
                 @EndedAt, @ConnectedDurationSeconds, @Disposition, @IsFinal, @IngestedAt, @UpdatedAt)
            """,
            new
            {
                Id = call.Id.ToString(),
                call.SourceRecordId,
                Direction = call.Direction.ToString(),
                call.DialledNumber,
                call.CallerId,
                call.StartedAt,
                call.AnsweredAt,
                call.EndedAt,
                call.ConnectedDurationSeconds,
                call.Disposition,
                call.IsFinal,
                call.IngestedAt,
                call.UpdatedAt,
            });

    private static async Task InsertAttributionAsync(MySqlConnection connection, DomainAttribution attribution) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO attributions
                (id, call_id, session_id, allocation_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES
                (@Id, @CallId, @SessionId, @AllocationId, @State, @Reason, @IsShadowDerived, 1, @DecidedAt)
            """,
            new
            {
                Id = attribution.Id.ToString(),
                CallId = attribution.CallId.ToString(),
                SessionId = attribution.SessionId?.ToString(),
                AllocationId = attribution.AllocationId?.ToString(),
                State = attribution.State.ToString(),
                attribution.Reason,
                attribution.IsShadowDerived,
                attribution.DecidedAt,
            });

    private static async Task<Guid> GetDefaultRuleIdAsync(MySqlConnection connection)
    {
        var id = await connection.ExecuteScalarAsync<string?>(
            "SELECT id FROM qualification_rules WHERE scope_type = 'Default' AND scope_ref IS NULL AND effective_end IS NULL LIMIT 1");
        return id is not null
            ? Guid.Parse(id)
            : throw new InvalidOperationException("No open-ended Default qualification rule seeded — run scripts/seed-dev-data.sql against this database.");
    }
}
