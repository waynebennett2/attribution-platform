using Attribution.Application.Attribution;
using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using MySqlConnector;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.IntegrationTests.Administration;

// FR-036: a reviewer resolves a manual review case either by crediting the call to a
// specific session or by confirming it stays unattributed; either way the resolution is a
// new superseding Attribution row (never an edit of the original decision), the review
// case itself closes, and — when the call being resolved was already published on the
// strength of its prior decision — the change is propagated under FR-044, exactly as a
// FR-045 restatement would be (CorrectionPropagationTests exercises CorrectionService's own
// retract/adjust logic in depth; this only confirms ReviewResolutionService actually wires
// into it).
public class ReviewResolutionTests : IAsyncLifetime
{
    private static readonly Guid DefaultQualificationRuleId = Guid.Parse("00000000-0000-0000-0000-000000000008");

    private ReviewResolutionService _resolutionService = null!;
    private RecordingGoogleAdsClient _googleAdsClient = null!;
    private IReviewCaseRepository _reviewCaseRepository = null!;
    private IAttributionRepository _attributionRepository = null!;

    public Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);

        _reviewCaseRepository = new ReviewCaseRepository(connectionFactory);
        _attributionRepository = new AttributionRepository(connectionFactory);
        var callRepository = new CallRepository(connectionFactory);
        var qualificationResultRepository = new QualificationResultRepository(connectionFactory);
        var allocationRepository = new AllocationRepository(connectionFactory);
        var alertRepository = new AlertRepository(connectionFactory);
        var sessionRepository = new SessionRepository(connectionFactory);
        var websiteRepository = new WebsiteRepository(connectionFactory);
        var ruleRepository = new QualificationRuleRepository(connectionFactory);
        var publicationRepository = new ConversionPublicationRepository(connectionFactory);
        var auditLogger = new AuditLogger(new AuditRepository(connectionFactory), new SystemActorContext());

        _googleAdsClient = new RecordingGoogleAdsClient();
        var correctionService = new CorrectionService(publicationRepository, sessionRepository, _googleAdsClient, auditLogger);
        var publicationService = new PublicationService(publicationRepository, sessionRepository);
        var qualificationService = new QualificationService(ruleRepository, qualificationResultRepository, sessionRepository, websiteRepository, publicationService);

        _resolutionService = new ReviewResolutionService(
            _reviewCaseRepository, callRepository, _attributionRepository, qualificationResultRepository,
            allocationRepository, alertRepository, qualificationService, correctionService);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ResolveByAttributingToSession_SupersedesTheAmbiguousDecision_AndClosesTheCase()
    {
        var (call, sessionId, _) = await SeedQualifyingCallWithAllocatedSessionAsync();
        var ambiguousAttribution = DomainAttribution.Ambiguous(call.Id, "multiple_allocation_windows_cover_call_start", call.StartedAt);
        await _attributionRepository.AddAsync(ambiguousAttribution);
        var reviewCase = ReviewCase.Open(call.Id, ambiguousAttribution.Id, DateTimeOffset.UtcNow);
        await _reviewCaseRepository.AddAsync(reviewCase);

        var resolvedAttribution = await _resolutionService.ResolveAsync(reviewCase.Id, sessionId, "reviewer@example.com", DateTimeOffset.UtcNow);

        Assert.Equal(AttributionState.Attributed, resolvedAttribution.State);
        Assert.Equal(sessionId, resolvedAttribution.SessionId);

        var (isCurrent, supersededReason) = await FetchSupersessionStateAsync(ambiguousAttribution.Id);
        Assert.False(isCurrent);
        Assert.Equal("manual_review_resolved", supersededReason);

        var closedCase = await _reviewCaseRepository.GetByIdAsync(reviewCase.Id);
        Assert.Equal(ReviewCaseStatus.Resolved, closedCase!.Status);
        Assert.Equal("reviewer@example.com", closedCase.ResolvedBy);
    }

    [Fact]
    public async Task ResolveConfirmingUnattributed_OnAnAlreadyPublishedCall_RetractsTheGoogleAdsConversion()
    {
        var (call, sessionId, allocationId) = await SeedQualifyingCallWithAllocatedSessionAsync();
        var publishedAttribution = DomainAttribution.Attributed(call.Id, sessionId, allocationId, call.StartedAt);
        await _attributionRepository.AddAsync(publishedAttribution);

        var publishedResult = QualificationResult.Decide(call.Id, publishedAttribution.Id, DefaultQualificationRuleId, isQualified: true, DateTimeOffset.UtcNow);
        var qualificationResultRepository = new QualificationResultRepository(new MySqlConnectionFactory(TestDatabase.ConnectionString));
        await qualificationResultRepository.AddAsync(publishedResult);

        var externalId = $"gclid-{Guid.NewGuid():N}";
        var publication = ConversionPublication.CreatePending(publishedResult.Id, PublicationDestination.GoogleAds, $"key-{Guid.NewGuid()}");
        publication.MarkSent(externalId, DateTimeOffset.UtcNow);
        var publicationRepository = new ConversionPublicationRepository(new MySqlConnectionFactory(TestDatabase.ConnectionString));
        await publicationRepository.AddAsync(publication);

        var reviewCase = ReviewCase.Open(call.Id, publishedAttribution.Id, DateTimeOffset.UtcNow);
        await _reviewCaseRepository.AddAsync(reviewCase);

        var resolvedAttribution = await _resolutionService.ResolveAsync(reviewCase.Id, chosenSessionId: null, "reviewer@example.com", DateTimeOffset.UtcNow);

        Assert.Equal(AttributionState.Unattributed, resolvedAttribution.State);
        Assert.Equal(new[] { externalId }, _googleAdsClient.Retracted);

        var closedCase = await _reviewCaseRepository.GetByIdAsync(reviewCase.Id);
        Assert.Equal(ReviewCaseStatus.Resolved, closedCase!.Status);
        Assert.Equal("confirmed_unattributed", closedCase.Resolution);
    }

    private static async Task<(bool IsCurrent, string? SupersededReason)> FetchSupersessionStateAsync(Guid id)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var row = await connection.QuerySingleAsync<(int IsCurrent, string? SupersededReason)>(
            "SELECT is_current AS IsCurrent, superseded_reason AS SupersededReason FROM attributions WHERE id = @Id",
            new { Id = id.ToString() });
        return (row.IsCurrent == 1, row.SupersededReason);
    }

    private static async Task<(Call Call, Guid SessionId, Guid AllocationId)> SeedQualifyingCallWithAllocatedSessionAsync()
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
                (@Id, 'Review Resolution Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });

        var poolId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Review Test Pool', 'website', @ScopeRef, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
            new { Id = poolId.ToString(), ScopeRef = websiteId.ToString() });

        var trackingNumberId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at) VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())",
            new { Id = trackingNumberId.ToString(), PoolId = poolId.ToString(), Did = $"+4416329{Random.Shared.Next(60000, 69999)}" });

        var visitorId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at) VALUES (@Id, @WebsiteId, UTC_TIMESTAMP())",
            new { Id = visitorId.ToString(), WebsiteId = websiteId.ToString() });

        var sessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO sessions (id, visitor_id, website_id, gclid, ga4_client_id, consent_state, provenance, started_at, expires_at)
            VALUES (@Id, @VisitorId, @WebsiteId, @Gclid, @Ga4ClientId, 'Granted', 'Ordinary', UTC_TIMESTAMP(), @ExpiresAt)
            """,
            new
            {
                Id = sessionId.ToString(), VisitorId = visitorId.ToString(), WebsiteId = websiteId.ToString(),
                Gclid = $"gclid-{Guid.NewGuid():N}", Ga4ClientId = $"ga4-{Guid.NewGuid():N}", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            });

        var allocationId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
            VALUES (@Id, @TrackingNumberId, @SessionId, @PoolId, UTC_TIMESTAMP(), @WindowEnd, 0, UTC_TIMESTAMP())
            """,
            new
            {
                Id = allocationId.ToString(), TrackingNumberId = trackingNumberId.ToString(), SessionId = sessionId.ToString(),
                PoolId = poolId.ToString(), WindowEnd = DateTimeOffset.UtcNow.AddHours(1),
            });

        var call = Call.Create(
            $"review-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", "+441632960999",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(90), 90, "answered", true, DateTimeOffset.UtcNow);
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

        return (call, sessionId, allocationId);
    }
}
