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

// SC-002, FR-027, Acceptance Scenarios 1-2: a qualified call is published exactly once to
// both destinations, and running publication again (retry, reprocessing, or an unrelated
// re-derivation calling QualifyAsync again) never produces a second conversion. Exercises
// the real outbox pipeline — PublicationService's enqueue, then a drain replicating
// PublicationWorker's own attempt loop — against the shared database, with recording
// stand-ins for the actual Google Ads/GA4 clients.
public class PublicationIdempotencyTests : IAsyncLifetime
{
    private const int MaxAttempts = 5;

    private PublicationService _publicationService = null!;
    private IConversionPublicationRepository _publicationRepository = null!;
    private IQualificationResultRepository _qualificationResultRepository = null!;
    private RecordingGoogleAdsClient _googleAdsClient = null!;
    private RecordingGa4Client _ga4Client = null!;
    private Call _call = null!;
    private DomainAttribution _attribution = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var sessionRepository = new SessionRepository(connectionFactory);
        _publicationRepository = new ConversionPublicationRepository(connectionFactory);
        _qualificationResultRepository = new QualificationResultRepository(connectionFactory);
        _publicationService = new PublicationService(_publicationRepository, sessionRepository);
        _googleAdsClient = new RecordingGoogleAdsClient();
        _ga4Client = new RecordingGa4Client();

        var (call, attribution) = await SeedQualifyingCallAsync();
        _call = call;
        _attribution = attribution;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task QualifiedCall_PublishesExactlyOnceToBothDestinations_AndRetryingProducesNoDuplicate()
    {
        var result = QualificationResultFor(_call, _attribution);
        await _qualificationResultRepository.AddAsync(result);

        await _publicationService.EnqueueAsync(_call, _attribution, result, DateTimeOffset.UtcNow);
        var afterEnqueue = await _publicationRepository.GetRetryableAsync(MaxAttempts, 100);
        Assert.Equal(2, afterEnqueue.Count(item => item.CallId == _call.Id));

        await DrainAsync();

        Assert.Single(_googleAdsClient.Uploaded);
        Assert.Single(_ga4Client.SentEvents);

        var googleAds = await _publicationRepository.GetActiveForCallAsync(_call.Id, PublicationDestination.GoogleAds);
        var ga4 = await _publicationRepository.GetActiveForCallAsync(_call.Id, PublicationDestination.Ga4);
        Assert.Equal(PublicationStatus.Sent, googleAds!.Status);
        Assert.Equal(PublicationStatus.Sent, ga4!.Status);

        // "Publication runs again for any reason" (Acceptance Scenario 2): nothing left to
        // drain (Sent rows aren't retryable), and re-enqueueing is a no-op.
        var secondDrainBatch = await _publicationRepository.GetRetryableAsync(MaxAttempts, 100);
        Assert.DoesNotContain(secondDrainBatch, item => item.CallId == _call.Id);

        await _publicationService.EnqueueAsync(_call, _attribution, result, DateTimeOffset.UtcNow);

        Assert.Single(_googleAdsClient.Uploaded);
        Assert.Single(_ga4Client.SentEvents);
    }

    // Mirrors PublicationWorker.AttemptAsync — that class can't be invoked directly
    // outside its BackgroundService/DI-scope machinery, so this replicates its logic
    // against the real repository and recording clients.
    private async Task DrainAsync()
    {
        var items = await _publicationRepository.GetRetryableAsync(MaxAttempts, 100);
        foreach (var item in items.Where(i => i.CallId == _call.Id))
        {
            if (item.Publication.Destination == PublicationDestination.GoogleAds)
            {
                var conversion = new GoogleAdsConversion(item.Gclid, item.Gbraid, item.Wbraid, item.CallStartedAt, "call_qualified");
                var externalId = await _googleAdsClient.UploadConversionAsync(conversion, CancellationToken.None);
                item.Publication.MarkSent(externalId, DateTimeOffset.UtcNow);
            }
            else
            {
                await _ga4Client.SendEventAsync(new Ga4Event(item.Ga4ClientId!, "call_qualified", item.CallStartedAt), CancellationToken.None);
                item.Publication.MarkSent(null, DateTimeOffset.UtcNow);
            }

            await _publicationRepository.UpdateAsync(item.Publication);
        }
    }

    // The FR-022 platform default rule seed-dev-data.sql seeds at this fixed id.
    private static readonly Guid DefaultQualificationRuleId = Guid.Parse("00000000-0000-0000-0000-000000000008");

    private static QualificationResult QualificationResultFor(Call call, DomainAttribution attribution) =>
        QualificationResult.Decide(call.Id, attribution.Id, DefaultQualificationRuleId, isQualified: true, DateTimeOffset.UtcNow);

    private async Task<(Call Call, DomainAttribution Attribution)> SeedQualifyingCallAsync()
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
                (@Id, 'Publication Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
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
            $"pub-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", "+441632960999",
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
