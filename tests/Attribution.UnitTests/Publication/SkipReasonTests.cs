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

// FR-026, Acceptance Scenario 4: a qualified call whose session carries no Google click
// identifier is not reported to Google Ads, and one with no GA4 client id is not reported
// to GA4 — each recorded with a reason rather than retried, since neither identifier can
// ever appear later for a session that never captured one.
public class SkipReasonTests
{
    private static Call QualifyingCall() => Call.Create(
        Guid.NewGuid().ToString(), CallDirection.Inbound, "+441632960001", "+441632960999",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(90), 90,
        "answered", true, DateTimeOffset.UtcNow);

    private static (PublicationService Service, FakeConversionPublicationRepository Publications, FakeSessionRepository Sessions)
        BuildService()
    {
        var sessions = new FakeSessionRepository();
        var publications = new FakeConversionPublicationRepository();
        return (new PublicationService(publications, sessions), publications, sessions);
    }

    private static async Task<(Call Call, DomainAttribution Attribution)> EnqueueAsync(
        PublicationService service, FakeConversionPublicationRepository publications, FakeSessionRepository sessions, Session session)
    {
        sessions.Sessions.Add(session);
        var call = QualifyingCall();
        var attribution = DomainAttribution.Attributed(call.Id, session.Id, Guid.NewGuid(), call.StartedAt);
        var result = QualificationResult.Decide(call.Id, attribution.Id, Guid.NewGuid(), isQualified: true, DateTimeOffset.UtcNow);
        publications.CallIdByQualificationResultId[result.Id] = call.Id;

        await service.EnqueueAsync(call, attribution, result, DateTimeOffset.UtcNow);
        return (call, attribution);
    }

    [Fact]
    public async Task SessionWithNoGoogleClickIdentifier_SkipsGoogleAds_WithReason_ButNotGa4()
    {
        var (service, publications, sessions) = BuildService();
        var session = Session.Create(
            Guid.NewGuid(), Guid.NewGuid(), ArrivalDetails.Empty with { Ga4ClientId = "ga4-client-1" },
            SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await EnqueueAsync(service, publications, sessions, session);

        var googleAds = publications.Publications.Single(p => p.Destination == PublicationDestination.GoogleAds);
        Assert.Equal(PublicationStatus.Skipped, googleAds.Status);
        Assert.Equal("no_google_click_identifier", googleAds.SkippedReason);

        var ga4 = publications.Publications.Single(p => p.Destination == PublicationDestination.Ga4);
        Assert.Equal(PublicationStatus.Pending, ga4.Status);
    }

    [Fact]
    public async Task SessionWithNoGa4ClientId_SkipsGa4_WithReason_ButNotGoogleAds()
    {
        var (service, publications, sessions) = BuildService();
        var session = Session.Create(
            Guid.NewGuid(), Guid.NewGuid(), ArrivalDetails.Empty with { Gclid = "gclid-1" },
            SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await EnqueueAsync(service, publications, sessions, session);

        var ga4 = publications.Publications.Single(p => p.Destination == PublicationDestination.Ga4);
        Assert.Equal(PublicationStatus.Skipped, ga4.Status);
        Assert.Equal("no_ga4_client_id", ga4.SkippedReason);

        var googleAds = publications.Publications.Single(p => p.Destination == PublicationDestination.GoogleAds);
        Assert.Equal(PublicationStatus.Pending, googleAds.Status);
    }

    [Fact]
    public async Task GbraidOrWbraid_AloneSatisfiesGoogleAds_EvenWithoutAGclid()
    {
        var (service, publications, sessions) = BuildService();
        var session = Session.Create(
            Guid.NewGuid(), Guid.NewGuid(), ArrivalDetails.Empty with { Gbraid = "gbraid-1", Ga4ClientId = "ga4-client-1" },
            SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await EnqueueAsync(service, publications, sessions, session);

        var googleAds = publications.Publications.Single(p => p.Destination == PublicationDestination.GoogleAds);
        Assert.Equal(PublicationStatus.Pending, googleAds.Status);
    }

    [Fact]
    public async Task SessionWithBothIdentifiers_SkipsNeitherDestination()
    {
        var (service, publications, sessions) = BuildService();
        var session = Session.Create(
            Guid.NewGuid(), Guid.NewGuid(), ArrivalDetails.Empty with { Gclid = "gclid-1", Ga4ClientId = "ga4-client-1" },
            SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await EnqueueAsync(service, publications, sessions, session);

        Assert.All(publications.Publications, p => Assert.Equal(PublicationStatus.Pending, p.Status));
    }
}
