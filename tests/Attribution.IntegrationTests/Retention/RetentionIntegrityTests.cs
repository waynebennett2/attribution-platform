using Attribution.Application.Administration;
using Attribution.Domain.Calls;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using MySqlConnector;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.IntegrationTests.Retention;

// FR-040: proves IRetentionRepository's real SQL against a real database — the eligibility
// queries actually find rows past their threshold, and de-identification/purge actually
// transform/remove exactly the row asked for. RetentionService's orchestration logic (the
// open-review-case skip, HMAC stability, erasure's unconditional-except-review behavior)
// is unit-tested against an in-memory fake instead (RetentionServiceTests) rather than
// here: this database is shared with every other integration test in the suite and never
// reset between runs (see the remote-db-for-tests convention), so calling the real
// system-wide sweep methods (DeIdentifyExpiredAsync/PurgeExpiredAsync, which scan the
// *entire* calls/visitors tables for anything older than a cutoff) from here would touch
// other tests' own deliberately-old-dated fixtures — ReportReconciliationTests picks dates
// up to hundreds of years in the past specifically to avoid colliding with other tests'
// *date ranges*, not with a retention sweep that ignores date ranges entirely. Every
// assertion below is instead scoped to IDs this test itself seeded.
public class RetentionIntegrityTests : IAsyncLifetime
{
    private IRetentionRepository _retentionRepository = null!;
    private ReportingService _reportingService = null!;

    public Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var unitOfWork = new UnitOfWork(connectionFactory);
        _retentionRepository = new RetentionRepository(connectionFactory, unitOfWork);
        _reportingService = new ReportingService(new ReportingRepository(connectionFactory));
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCallsEligibleForDeIdentification_FindsAnOldCall_ButNotAFreshOne()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, _, _, oldCallId, _) = await SeedFullyQualifiedCallAsync(startedAt: now.AddMonths(-2));
        var (_, _, _, freshCallId, _) = await SeedFullyQualifiedCallAsync(startedAt: now);

        var eligible = await _retentionRepository.GetCallsEligibleForDeIdentificationAsync(now.AddMonths(-1));

        Assert.Contains(eligible, c => c.Id == oldCallId);
        Assert.DoesNotContain(eligible, c => c.Id == freshCallId);
    }

    [Fact]
    public async Task DeIdentifyCall_MasksTheCallerId_ButLeavesReportTotalsForThatPeriodUnchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, _, _, callId, originalCallerId) = await SeedFullyQualifiedCallAsync(startedAt: now.AddMonths(-2));
        var reportDay = DateOnly.FromDateTime(now.AddMonths(-2).UtcDateTime);
        var callIdText = callId.ToString();

        var before = await _reportingService.CallsAsync(reportDay, reportDay, state: null, q: null);
        var beforeRow = Assert.Single(before.Rows.Where(r => (string)r["call_id"]! == callIdText));
        Assert.Equal(originalCallerId, beforeRow["caller_id"]);

        await _retentionRepository.DeIdentifyCallAsync(callId, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd", now);

        var after = await _reportingService.CallsAsync(reportDay, reportDay, state: null, q: null);
        Assert.Equal(before.Rows.Count, after.Rows.Count);
        Assert.Equal(before.Totals, after.Totals);
        var afterRow = Assert.Single(after.Rows.Where(r => (string)r["call_id"]! == callIdText));
        Assert.NotEqual(originalCallerId, afterRow["caller_id"]);
    }

    [Fact]
    public async Task DeIdentifyVisitor_ClearsSessionTrackingIdentifiers_AndMarksTheVisitorProcessed()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, visitorId, sessionId, _, _) = await SeedFullyQualifiedCallAsync(startedAt: now.AddMonths(-2));

        await _retentionRepository.DeIdentifyVisitorAsync(visitorId, now);

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var visitorDeIdentifiedAt = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT de_identified_at FROM visitors WHERE id = @Id", new { Id = visitorId.ToString() });
        Assert.NotNull(visitorDeIdentifiedAt);

        var sessionGclid = await connection.ExecuteScalarAsync<string?>(
            "SELECT gclid FROM sessions WHERE id = @Id", new { Id = sessionId.ToString() });
        Assert.Null(sessionGclid);
    }

    [Fact]
    public async Task HasOpenReviewCase_ReflectsTheReviewCasesCurrentStatus()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, _, _, callId, _) = await SeedFullyQualifiedCallAsync(startedAt: now);

        Assert.False(await _retentionRepository.HasOpenReviewCaseAsync(callId));

        await using (var connection = new MySqlConnection(TestDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            var attributionId = await connection.ExecuteScalarAsync<string>(
                "SELECT id FROM attributions WHERE call_id = @CallId", new { CallId = callId.ToString() });
            await connection.ExecuteAsync(
                """
                INSERT INTO review_cases (id, call_id, attribution_id, status, opened_at)
                VALUES (@Id, @CallId, @AttributionId, 'Open', UTC_TIMESTAMP())
                """,
                new { Id = Guid.NewGuid().ToString(), CallId = callId.ToString(), AttributionId = attributionId });
        }

        Assert.True(await _retentionRepository.HasOpenReviewCaseAsync(callId));
    }

    [Fact]
    public async Task PurgeCall_CascadesTheDeleteThroughEveryDependentTable()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, _, _, callId, _) = await SeedFullyQualifiedCallAsync(startedAt: now.AddMonths(-3));

        await _retentionRepository.PurgeCallAsync(callId);

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var callIdText = callId.ToString();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM calls WHERE id = @Id", new { Id = callIdText }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM attributions WHERE call_id = @Id", new { Id = callIdText }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM qualification_results WHERE call_id = @Id", new { Id = callIdText }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            WHERE qr.call_id = @Id
            """,
            new { Id = callIdText }));
    }

    [Fact]
    public async Task PurgeAuditLogOlderThan_DeletesOnlyEntriesPastTheCutoff()
    {
        var now = DateTimeOffset.UtcNow;
        var oldEntryId = Guid.NewGuid();
        var recentEntryId = Guid.NewGuid();

        await using (var connection = new MySqlConnection(TestDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
                VALUES (@Id, 'test', 'TestAction', 'TestTarget', @Id, NULL, '{}', @OccurredAt)
                """,
                new { Id = oldEntryId.ToString(), OccurredAt = now.AddYears(-2) });
            await connection.ExecuteAsync(
                """
                INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
                VALUES (@Id, 'test', 'TestAction', 'TestTarget', @Id, NULL, '{}', @OccurredAt)
                """,
                new { Id = recentEntryId.ToString(), OccurredAt = now });
        }

        // Nothing else this session ever backdates an audit entry (AuditLogger always
        // stamps occurred_at = UtcNow), so a 1-year cutoff cannot reach anything but this
        // test's own oldEntryId on a freshly-reset database.
        await _retentionRepository.PurgeAuditLogOlderThanAsync(now.AddYears(-1));

        await using var verifyConnection = new MySqlConnection(TestDatabase.ConnectionString);
        await verifyConnection.OpenAsync();
        Assert.Equal(0, await verifyConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE id = @Id", new { Id = oldEntryId.ToString() }));
        Assert.Equal(1, await verifyConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE id = @Id", new { Id = recentEntryId.ToString() }));
    }

    internal static async Task<(Guid WebsiteId, Guid VisitorId, Guid SessionId, Guid CallId, string CallerId)> SeedFullyQualifiedCallAsync(
        DateTimeOffset startedAt)
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
                (@Id, 'Retention Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, @FirstSeenAt)",
            new { Id = visitorId.ToString(), WebsiteId = websiteId.ToString(), FirstSeenAt = startedAt });

        var sessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions (id, visitor_id, website_id, gclid, ga4_client_id, consent_state, provenance, started_at, expires_at)
            VALUES (@Id, @VisitorId, @WebsiteId, @Gclid, @Ga4ClientId, 'Granted', 'Ordinary', @StartedAt, @ExpiresAt)
            """,
            new
            {
                Id = sessionId.ToString(), VisitorId = visitorId.ToString(), WebsiteId = websiteId.ToString(),
                Gclid = $"gclid-{Guid.NewGuid():N}", Ga4ClientId = $"ga4-{Guid.NewGuid():N}",
                StartedAt = startedAt, ExpiresAt = startedAt.AddDays(1),
            });

        var callerId = $"+441632{Random.Shared.Next(100000, 999999)}";
        var call = Call.Create(
            $"retention-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", callerId,
            startedAt, startedAt, startedAt.AddSeconds(90), 90, "answered", true, startedAt);
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
                Id = call.Id.ToString(), call.SourceRecordId, Direction = call.Direction.ToString(), call.DialledNumber,
                call.CallerId, call.StartedAt, call.AnsweredAt, call.EndedAt, call.ConnectedDurationSeconds,
                call.Disposition, call.IsFinal, call.IngestedAt, call.UpdatedAt,
            });

        var attribution = DomainAttribution.Attributed(call.Id, sessionId, Guid.NewGuid(), call.StartedAt);
        await connection.ExecuteAsync(
            """
            INSERT INTO attributions (id, call_id, session_id, allocation_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES (@Id, @CallId, @SessionId, NULL, @State, @Reason, 0, 1, @DecidedAt)
            """,
            new
            {
                Id = attribution.Id.ToString(), CallId = call.Id.ToString(), SessionId = sessionId.ToString(),
                State = attribution.State.ToString(), attribution.Reason, attribution.DecidedAt,
            });

        var qualificationResult = QualificationResult.Decide(
            call.Id, attribution.Id, Guid.Parse("00000000-0000-0000-0000-000000000008"), isQualified: true, startedAt);
        await connection.ExecuteAsync(
            """
            INSERT INTO qualification_results (id, call_id, attribution_id, qualification_rule_id, is_qualified, is_current, decided_at)
            VALUES (@Id, @CallId, @AttributionId, @RuleId, 1, 1, @DecidedAt)
            """,
            new
            {
                Id = qualificationResult.Id.ToString(), CallId = call.Id.ToString(), AttributionId = attribution.Id.ToString(),
                RuleId = "00000000-0000-0000-0000-000000000008", qualificationResult.DecidedAt,
            });

        var publication = ConversionPublication.CreatePending(qualificationResult.Id, PublicationDestination.GoogleAds, $"key-{Guid.NewGuid()}");
        publication.MarkSent($"gclid-ext-{Guid.NewGuid():N}", startedAt);
        await connection.ExecuteAsync(
            """
            INSERT INTO conversion_publications
                (id, qualification_result_id, destination, idempotency_key, status, external_id, sent_at)
            VALUES (@Id, @QualificationResultId, @Destination, @IdempotencyKey, @Status, @ExternalId, @SentAt)
            """,
            new
            {
                Id = publication.Id.ToString(), QualificationResultId = qualificationResult.Id.ToString(),
                Destination = publication.Destination.ToString(), publication.IdempotencyKey,
                Status = publication.Status.ToString(), publication.ExternalId, publication.SentAt,
            });

        return (websiteId, visitorId, sessionId, call.Id, callerId);
    }
}
