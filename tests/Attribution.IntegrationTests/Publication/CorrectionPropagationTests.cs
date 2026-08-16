using Attribution.Application.Publication;
using Attribution.Domain.Calls;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using MySqlConnector;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.IntegrationTests.Publication;

// FR-044, Acceptance Scenario 6: once a call's already-published qualification changes
// (here: a review resolution or restatement makes it no longer qualify), the Google Ads
// conversion is retracted, the GA4 event is recorded as unpropagatable, both are audited,
// and repeating the correction changes nothing further at either destination.
public class CorrectionPropagationTests : IAsyncLifetime
{
    private static readonly Guid DefaultQualificationRuleId = Guid.Parse("00000000-0000-0000-0000-000000000008");

    private CorrectionService _correctionService = null!;
    private IConversionPublicationRepository _publicationRepository = null!;
    private RecordingGoogleAdsClient _googleAdsClient = null!;
    private Call _call = null!;
    private DomainAttribution _attribution = null!;
    private QualificationResult _publishedResult = null!;
    private string _googleAdsExternalId = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var sessionRepository = new SessionRepository(connectionFactory);
        _publicationRepository = new ConversionPublicationRepository(connectionFactory);
        var qualificationResultRepository = new QualificationResultRepository(connectionFactory);
        var auditLogger = new AuditLogger(new AuditRepository(connectionFactory), new SystemActorContext());
        _googleAdsClient = new RecordingGoogleAdsClient();
        _correctionService = new CorrectionService(_publicationRepository, sessionRepository, _googleAdsClient, auditLogger);

        (_call, _attribution) = await SeedQualifyingCallAsync();

        _publishedResult = QualificationResult.Decide(_call.Id, _attribution.Id, DefaultQualificationRuleId, isQualified: true, DateTimeOffset.UtcNow);
        await qualificationResultRepository.AddAsync(_publishedResult);

        // Simulate both destinations having already been published to, bypassing the
        // outbox drain — these tests exercise correction, not the publish step itself
        // (covered by PublicationIdempotencyTests).
        _googleAdsExternalId = $"gclid-{Guid.NewGuid():N}";
        var googleAdsPublication = ConversionPublication.CreatePending(_publishedResult.Id, PublicationDestination.GoogleAds, $"key-{Guid.NewGuid()}");
        googleAdsPublication.MarkSent(_googleAdsExternalId, DateTimeOffset.UtcNow);
        await _publicationRepository.AddAsync(googleAdsPublication);

        var ga4Publication = ConversionPublication.CreatePending(_publishedResult.Id, PublicationDestination.Ga4, $"key-{Guid.NewGuid()}");
        ga4Publication.MarkSent(null, DateTimeOffset.UtcNow);
        await _publicationRepository.AddAsync(ga4Publication);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NoLongerQualified_RetractsGoogleAds_MarksGa4Unpropagatable_BothAudited_AndIdempotent()
    {
        await _correctionService.CorrectIfNeededAsync(_call, _attribution, _publishedResult, newResult: null, DateTimeOffset.UtcNow);

        // Fetched by raw id rather than GetActiveForCallAsync: that method deliberately
        // excludes Retracted rows (it answers "what, if anything, still needs
        // correcting/enqueuing"), so it can't be used to inspect a row's post-retraction state.
        var googleAdsStatus = await FetchStatusAsync(PublicationDestination.GoogleAds);
        Assert.Equal(PublicationStatus.Retracted.ToString(), googleAdsStatus);
        Assert.Equal(new[] { _googleAdsExternalId }, _googleAdsClient.Retracted);

        var ga4 = await _publicationRepository.GetActiveForCallAsync(_call.Id, PublicationDestination.Ga4);
        Assert.Equal(PublicationStatus.Sent, ga4!.Status); // unchanged — GA4 has no retraction
        Assert.Equal(CorrectionType.Unpropagatable, ga4.Correction!.Type);
        Assert.False(ga4.Correction.DestinationAccepted);

        var auditCount = await CountCorrectionAuditEntriesAsync(ga4.Id);
        Assert.Equal(2, auditCount); // one per destination

        // Repeating the correction (Acceptance Scenario 6) must change nothing further.
        await _correctionService.CorrectIfNeededAsync(_call, _attribution, _publishedResult, newResult: null, DateTimeOffset.UtcNow);

        Assert.Single(_googleAdsClient.Retracted); // no second retraction call
        Assert.Equal(2, await CountCorrectionAuditEntriesAsync(ga4.Id)); // no new audit entries
    }

    private async Task<string?> FetchStatusAsync(PublicationDestination destination)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>(
            """
            SELECT cp.status FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            WHERE qr.call_id = @CallId AND cp.destination = @Destination
            """,
            new { CallId = _call.Id.ToString(), Destination = destination.ToString() });
    }

    private async Task<int> CountCorrectionAuditEntriesAsync(Guid ga4PublicationId)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var googleAdsId = await connection.ExecuteScalarAsync<string>(
            """
            SELECT cp.id FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            WHERE qr.call_id = @CallId AND cp.destination = 'GoogleAds'
            """,
            new { CallId = _call.Id.ToString() });
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE action = 'CorrectConversionPublication' AND target_id IN @Ids",
            new { Ids = new[] { googleAdsId, ga4PublicationId.ToString() } });
    }

    private static async Task<(Call Call, DomainAttribution Attribution)> SeedQualifyingCallAsync()
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
                (@Id, 'Correction Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = visitorId.ToString(), WebsiteId = websiteId.ToString() });

        var sessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions
                (id, visitor_id, website_id, gclid, ga4_client_id, consent_state, provenance, started_at, expires_at)
            VALUES
                (@Id, @VisitorId, @WebsiteId, @Gclid, @Ga4ClientId, 'Granted', 'Ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new
            {
                Id = sessionId.ToString(), VisitorId = visitorId.ToString(), WebsiteId = websiteId.ToString(),
                Gclid = $"gclid-{Guid.NewGuid():N}", Ga4ClientId = $"ga4-{Guid.NewGuid():N}",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            });

        var call = Call.Create(
            $"corr-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", "+441632960999",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(90), 90,
            "answered", true, DateTimeOffset.UtcNow);
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
            INSERT INTO attributions
                (id, call_id, session_id, allocation_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES
                (@Id, @CallId, @SessionId, NULL, @State, @Reason, 0, 1, @DecidedAt)
            """,
            new
            {
                Id = attribution.Id.ToString(), CallId = call.Id.ToString(), SessionId = sessionId.ToString(),
                State = attribution.State.ToString(), attribution.Reason, attribution.DecidedAt,
            });

        return (call, attribution);
    }
}
