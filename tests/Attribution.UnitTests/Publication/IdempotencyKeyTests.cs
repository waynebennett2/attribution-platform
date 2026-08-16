using System;
using Attribution.Application.Publication;
using Attribution.Domain.Calls;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using Attribution.UnitTests.TestSupport;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.UnitTests.Publication;

// FR-027: idempotency-key generation, scoped to one publish episode. Retries/reprocessing
// within an episode must never enqueue a second row; a genuine retract-then-requalify
// (a new episode) must get a fresh key.
public class IdempotencyKeyTests
{
    private static Call QualifyingCall() => Call.Create(
        Guid.NewGuid().ToString(), CallDirection.Inbound, "+441632960001", "+441632960999",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(90), 90,
        "answered", true, DateTimeOffset.UtcNow);

    private static Session SessionWithIdentifiers() => Session.Create(
        Guid.NewGuid(), Guid.NewGuid(),
        ArrivalDetails.Empty with { Gclid = "gclid-1", Ga4ClientId = "ga4-client-1" },
        SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

    private static QualificationResult Qualified(Guid callId, Guid attributionId) =>
        QualificationResult.Decide(callId, attributionId, Guid.NewGuid(), isQualified: true, DateTimeOffset.UtcNow);

    [Fact]
    public async Task EnqueueingTheSameQualificationTwice_ReusesTheSameRow_NoDuplicate()
    {
        var sessions = new FakeSessionRepository();
        var session = SessionWithIdentifiers();
        sessions.Sessions.Add(session);
        var publications = new FakeConversionPublicationRepository();
        var service = new PublicationService(publications, sessions);

        var call = QualifyingCall();
        var attribution = DomainAttribution.Attributed(call.Id, session.Id, Guid.NewGuid(), call.StartedAt);
        var result = Qualified(call.Id, attribution.Id);
        publications.CallIdByQualificationResultId[result.Id] = call.Id;

        await service.EnqueueAsync(call, attribution, result, DateTimeOffset.UtcNow);
        var afterFirst = publications.Publications.Count;
        var keysAfterFirst = publications.Publications.Select(p => p.IdempotencyKey).ToList();

        // Simulates QualifyAsync running again for an unrelated reason (e.g. a
        // re-derivation that left is_qualified unchanged) — must be a pure no-op.
        await service.EnqueueAsync(call, attribution, result, DateTimeOffset.UtcNow);

        Assert.Equal(afterFirst, publications.Publications.Count);
        Assert.Equal(keysAfterFirst, publications.Publications.Select(p => p.IdempotencyKey));
    }

    [Fact]
    public async Task ARetractThenRequalify_GetsAFreshIdempotencyKey_ANewEpisode()
    {
        var sessions = new FakeSessionRepository();
        var session = SessionWithIdentifiers();
        sessions.Sessions.Add(session);
        var publications = new FakeConversionPublicationRepository();
        var service = new PublicationService(publications, sessions);

        var call = QualifyingCall();
        var attribution = DomainAttribution.Attributed(call.Id, session.Id, Guid.NewGuid(), call.StartedAt);
        var firstResult = Qualified(call.Id, attribution.Id);
        publications.CallIdByQualificationResultId[firstResult.Id] = call.Id;

        await service.EnqueueAsync(call, attribution, firstResult, DateTimeOffset.UtcNow);
        var googleAdsPublication = publications.Publications.Single(p => p.Destination == PublicationDestination.GoogleAds);
        var firstKey = googleAdsPublication.IdempotencyKey;

        // The episode ends: the conversion is retracted (FR-044).
        googleAdsPublication.MarkRetracted("no_longer_qualified", DateTimeOffset.UtcNow);

        // A later, genuinely new qualification for the same call.
        var secondResult = Qualified(call.Id, attribution.Id);
        publications.CallIdByQualificationResultId[secondResult.Id] = call.Id;
        await service.EnqueueAsync(call, attribution, secondResult, DateTimeOffset.UtcNow);

        var googleAdsPublications = publications.Publications.Where(p => p.Destination == PublicationDestination.GoogleAds).ToList();
        Assert.Equal(2, googleAdsPublications.Count); // the retracted row, plus a fresh one
        var secondKey = googleAdsPublications.Single(p => p.Status != PublicationStatus.Retracted).IdempotencyKey;
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public async Task UnqualifiedResult_NeverEnqueuesAnything()
    {
        var sessions = new FakeSessionRepository();
        var session = SessionWithIdentifiers();
        sessions.Sessions.Add(session);
        var publications = new FakeConversionPublicationRepository();
        var service = new PublicationService(publications, sessions);

        var call = QualifyingCall();
        var attribution = DomainAttribution.Attributed(call.Id, session.Id, Guid.NewGuid(), call.StartedAt);
        var result = QualificationResult.Decide(call.Id, attribution.Id, Guid.NewGuid(), isQualified: false, DateTimeOffset.UtcNow);

        await service.EnqueueAsync(call, attribution, result, DateTimeOffset.UtcNow);

        Assert.Empty(publications.Publications);
    }
}
