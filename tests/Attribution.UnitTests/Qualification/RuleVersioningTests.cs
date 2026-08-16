using System;
using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Attribution.UnitTests.TestSupport;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.UnitTests.Qualification;

// FR-024: rule versioning, effective-period contiguity, and most-specific-scope
// resolution (campaign overrides website overrides the platform default).
public class RuleVersioningTests
{
    [Fact]
    public async Task FirstVersionInAScope_IsVersion1_AndOpenEnded()
    {
        var repository = new FakeQualificationRuleRepository();
        var service = new RuleVersioningService(repository);

        var rule = await service.CreateVersionAsync(
            QualificationScopeType.Default, null, QualificationConditions.Default,
            effectiveStart: DateTimeOffset.UtcNow, "admin-1", DateTimeOffset.UtcNow);

        Assert.Equal(1, rule.Version);
        Assert.Null(rule.EffectiveEnd);
    }

    [Fact]
    public async Task SecondVersion_ClosesThePriorVersion_AtExactlyItsOwnEffectiveStart()
    {
        var repository = new FakeQualificationRuleRepository();
        var service = new RuleVersioningService(repository);
        var t0 = DateTimeOffset.UtcNow;

        var v1 = await service.CreateVersionAsync(QualificationScopeType.Default, null, QualificationConditions.Default, t0, "admin-1", t0);
        var v2Start = t0.AddDays(7);
        var v2 = await service.CreateVersionAsync(
            QualificationScopeType.Default, null, QualificationConditions.Default with { MinConnectedDurationSeconds = 90 },
            v2Start, "admin-1", v2Start);

        Assert.Equal(v2Start, v1.EffectiveEnd); // exact contiguity — no gap, no overlap
        Assert.Equal(2, v2.Version);
        Assert.Null(v2.EffectiveEnd);
        Assert.True(v1.IsInForceAt(v2Start.AddSeconds(-1)));
        Assert.False(v1.IsInForceAt(v2Start));
        Assert.True(v2.IsInForceAt(v2Start));
    }

    [Fact]
    public async Task NewVersionStartingAtOrBeforeTheCurrentVersionsStart_IsRejected()
    {
        var repository = new FakeQualificationRuleRepository();
        var service = new RuleVersioningService(repository);
        var t0 = DateTimeOffset.UtcNow;
        await service.CreateVersionAsync(QualificationScopeType.Default, null, QualificationConditions.Default, t0, "admin-1", t0);

        // Would leave a gap: nothing would cover the instants between the new start and t0.
        await Assert.ThrowsAsync<QualificationRuleContiguityException>(() =>
            service.CreateVersionAsync(QualificationScopeType.Default, null, QualificationConditions.Default, t0.AddDays(-1), "admin-1", t0));
    }

    [Fact]
    public async Task DeletingANotYetEffectiveFutureVersion_ReopensItsPredecessor()
    {
        var repository = new FakeQualificationRuleRepository();
        var service = new RuleVersioningService(repository);
        var t0 = DateTimeOffset.UtcNow.AddDays(-30);
        var v1 = await service.CreateVersionAsync(QualificationScopeType.Default, null, QualificationConditions.Default, t0, "admin-1", t0);

        var futureStart = DateTimeOffset.UtcNow.AddDays(7);
        var v2 = await service.CreateVersionAsync(
            QualificationScopeType.Default, null, QualificationConditions.Default with { MinConnectedDurationSeconds = 90 },
            futureStart, "admin-1", DateTimeOffset.UtcNow);
        Assert.Equal(futureStart, v1.EffectiveEnd);

        await service.DeleteFutureVersionAsync(v2.Id, DateTimeOffset.UtcNow);

        Assert.Null(v1.EffectiveEnd);
        Assert.DoesNotContain(repository.Rules, r => r.Id == v2.Id);
    }

    [Fact]
    public async Task DeletingALiveOrPastVersion_IsRejected()
    {
        var repository = new FakeQualificationRuleRepository();
        var service = new RuleVersioningService(repository);
        var t0 = DateTimeOffset.UtcNow.AddDays(-1);
        var v1 = await service.CreateVersionAsync(QualificationScopeType.Default, null, QualificationConditions.Default, t0, "admin-1", t0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteFutureVersionAsync(v1.Id, DateTimeOffset.UtcNow));
    }
}

// FR-024: "One active rule per scope, most-specific wins" — a matching campaign or
// website scope overrides the platform default; campaign, being narrower, wins over website.
public class MostSpecificScopeResolutionTests
{
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(90);

    private static DomainAttribution AttributedTo(Guid sessionId) =>
        DomainAttribution.Attributed(Guid.NewGuid(), sessionId, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static Call QualifyingCall() => Call.Create(
        Guid.NewGuid().ToString(), CallDirection.Inbound, "+441632960001", "+441632960999",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(LongEnough),
        (int)LongEnough.TotalSeconds, "answered", true, DateTimeOffset.UtcNow);

    private static QualificationRule Rule(QualificationScopeType scopeType, string? scopeRef, bool isQualified) =>
        QualificationRule.Create(
            scopeType, scopeRef, 1,
            // AnsweredRequired only, deliberately no duration/direction constraint, purely
            // to make which *rule* judged the call distinguishable by its outcome.
            new QualificationConditions(null, isQualified, null, null),
            DateTimeOffset.UtcNow.AddYears(-1), null, "admin-1", DateTimeOffset.UtcNow.AddYears(-1));

    [Fact]
    public async Task WebsiteScopedRule_OverridesThePlatformDefault()
    {
        var website = Website.Create("Test", new[] { "https://example.com" }, "+441632960000", "UTC");
        var websites = new FakeWebsiteRepository();
        websites.Websites.Add(website);
        var session = Session.Create(Guid.NewGuid(), website.Id, ArrivalDetails.Empty, SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        var sessions = new FakeSessionRepository();
        sessions.Sessions.Add(session);

        var rules = new FakeQualificationRuleRepository();
        rules.Rules.Add(Rule(QualificationScopeType.Default, null, isQualified: false)); // would reject
        rules.Rules.Add(Rule(QualificationScopeType.Website, website.Id.ToString(), isQualified: true)); // would accept

        var results = new FakeQualificationResultRepository();
        var publicationService = new PublicationService(new FakeConversionPublicationRepository(), sessions);
        var service = new QualificationService(rules, results, sessions, websites, publicationService);

        var result = await service.QualifyAsync(QualifyingCall(), AttributedTo(session.Id), DateTimeOffset.UtcNow);

        Assert.True(result.IsQualified);
    }

    [Fact]
    public async Task CampaignScopedRule_OverridesAWebsiteScopedRule()
    {
        var website = Website.Create("Test", new[] { "https://example.com" }, "+441632960000", "UTC");
        var websites = new FakeWebsiteRepository();
        websites.Websites.Add(website);
        var arrival = ArrivalDetails.Empty with { UtmCampaign = "spring-sale" };
        var session = Session.Create(Guid.NewGuid(), website.Id, arrival, SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        var sessions = new FakeSessionRepository();
        sessions.Sessions.Add(session);

        var rules = new FakeQualificationRuleRepository();
        rules.Rules.Add(Rule(QualificationScopeType.Default, null, isQualified: false));
        rules.Rules.Add(Rule(QualificationScopeType.Website, website.Id.ToString(), isQualified: false));
        rules.Rules.Add(Rule(QualificationScopeType.Campaign, "spring-sale", isQualified: true));

        var results = new FakeQualificationResultRepository();
        var publicationService = new PublicationService(new FakeConversionPublicationRepository(), sessions);
        var service = new QualificationService(rules, results, sessions, websites, publicationService);

        var result = await service.QualifyAsync(QualifyingCall(), AttributedTo(session.Id), DateTimeOffset.UtcNow);

        Assert.True(result.IsQualified);
    }

    [Fact]
    public async Task NoScopedRuleMatches_FallsBackToThePlatformDefault()
    {
        var website = Website.Create("Test", new[] { "https://example.com" }, "+441632960000", "UTC");
        var websites = new FakeWebsiteRepository();
        websites.Websites.Add(website);
        var session = Session.Create(Guid.NewGuid(), website.Id, ArrivalDetails.Empty, SessionProvenance.Ordinary, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        var sessions = new FakeSessionRepository();
        sessions.Sessions.Add(session);

        var rules = new FakeQualificationRuleRepository();
        rules.Rules.Add(Rule(QualificationScopeType.Default, null, isQualified: true));

        var results = new FakeQualificationResultRepository();
        var publicationService = new PublicationService(new FakeConversionPublicationRepository(), sessions);
        var service = new QualificationService(rules, results, sessions, websites, publicationService);

        var result = await service.QualifyAsync(QualifyingCall(), AttributedTo(session.Id), DateTimeOffset.UtcNow);

        Assert.True(result.IsQualified);
    }
}
