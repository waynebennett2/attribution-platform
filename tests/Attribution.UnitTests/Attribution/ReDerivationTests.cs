using System;
using Attribution.Application.Attribution;
using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Pools;
using Attribution.Domain.Qualification;
using Attribution.UnitTests.TestSupport;
using Xunit;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.UnitTests.Attribution;

// FR-045: a call 8x8 reports as changed (still in progress at the previous ingestion, or
// later corrected) must be updated in place, its attribution re-derived, and the
// superseded decision retained as history — and re-ingesting an unchanged record must be
// a complete no-op, which is what makes repeated ingestion idempotent.
public class ReDerivationTests
{
    private static readonly TimeSpan Extension = TimeSpan.FromMinutes(30);
    private const string Did = "+441632960001";

    private static (ReDerivationService Service, FakeCallRepository Calls, FakeAttributionRepository Attributions)
        BuildService(FakeTrackingNumberRepository? trackingNumbers = null, FakeAllocationRepository? allocations = null)
    {
        var calls = new FakeCallRepository();
        var attributions = new FakeAttributionRepository();
        var attributionService = new AttributionService(
            trackingNumbers ?? new FakeTrackingNumberRepository(),
            allocations ?? new FakeAllocationRepository(),
            attributions,
            new FakeReviewCaseRepository());

        // A platform default qualification rule always exists in production; seeded here
        // so re-attributing to a session with no matching website/campaign rule still has
        // something to fall back to, exactly as it would for real.
        var rules = new FakeQualificationRuleRepository();
        rules.Rules.Add(QualificationRule.Create(
            QualificationScopeType.Default, null, 1, QualificationConditions.Default,
            DateTimeOffset.UtcNow.AddYears(-1), null, "seed", DateTimeOffset.UtcNow.AddYears(-1)));
        var publicationService = new PublicationService(new FakeConversionPublicationRepository(), new FakeSessionRepository());
        var qualificationService = new QualificationService(
            rules, new FakeQualificationResultRepository(), new FakeSessionRepository(), new FakeWebsiteRepository(), publicationService);
        var correctionService = new CorrectionService(
            new FakeConversionPublicationRepository(), new FakeSessionRepository(), new FakeGoogleAdsClient(), new FakeAuditLogger());

        var reDerivationService = new ReDerivationService(
            calls, attributions, new FakeQualificationResultRepository(), attributionService, qualificationService, correctionService);
        return (reDerivationService, calls, attributions);
    }

    private static Analytics8x8CallRecord InProgressRecord(DateTimeOffset startedAt) => new(
        SourceRecordId: "cdr-1",
        Direction: CallDirection.Inbound,
        DialledNumber: Did,
        CallerId: "+441632960999",
        StartedAt: startedAt,
        AnsweredAt: startedAt.AddSeconds(2),
        EndedAt: null,
        ConnectedDurationSeconds: null,
        Disposition: null,
        IsFinal: false);

    [Fact]
    public async Task UnchangedRecord_IsANoOp_AndCreatesNoNewAttribution()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var record = InProgressRecord(startedAt);
        var call = Call.Create(
            record.SourceRecordId, record.Direction, record.DialledNumber, record.CallerId, record.StartedAt,
            record.AnsweredAt, record.EndedAt, record.ConnectedDurationSeconds, record.Disposition, record.IsFinal,
            ingestedAt: startedAt);

        var (service, calls, attributions) = BuildService();
        calls.Calls.Add(call);

        var result = await service.ReDeriveIfChangedAsync(call, record, DateTimeOffset.UtcNow);

        Assert.Null(result);
        Assert.Empty(attributions.Attributions);
    }

    [Fact]
    public async Task ChangedRecord_SupersedesTheCurrentAttribution_AndDecidesAFreshOne()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var record = InProgressRecord(startedAt);
        var call = Call.Create(
            record.SourceRecordId, record.Direction, record.DialledNumber, record.CallerId, record.StartedAt,
            record.AnsweredAt, record.EndedAt, record.ConnectedDurationSeconds, record.Disposition, record.IsFinal,
            ingestedAt: startedAt);

        var trackingNumber = TrackingNumber.Create(Guid.NewGuid(), Did);
        var trackingNumbers = new FakeTrackingNumberRepository();
        trackingNumbers.Numbers.Add(trackingNumber);

        var session = Guid.NewGuid();
        var allocation = DomainAllocation.Create(
            trackingNumber.Id, session, Guid.NewGuid(),
            windowStart: startedAt.AddMinutes(-5), sessionExpiresAt: startedAt.AddMinutes(30), Extension);
        var allocations = new FakeAllocationRepository();
        allocations.Allocations.Add(allocation);

        var (service, calls, attributions) = BuildService(trackingNumbers, allocations);
        calls.Calls.Add(call);
        var originalAttribution = DomainAttribution.Attributed(call.Id, session, allocation.Id, startedAt);
        attributions.Attributions.Add(originalAttribution);

        // The source now reports the call as finished — a genuinely changed record.
        var completedRecord = record with { EndedAt = startedAt.AddSeconds(90), ConnectedDurationSeconds = 88, Disposition = "answered", IsFinal = true };

        var result = await service.ReDeriveIfChangedAsync(call, completedRecord, DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        Assert.NotEqual(originalAttribution.Id, result!.Id);
        Assert.Equal(AttributionState.Attributed, result.State);
        Assert.Equal(session, result.SessionId);

        // The prior decision is retained as history, not overwritten.
        Assert.False(originalAttribution.IsCurrent);
        Assert.Equal("call_record_restated", originalAttribution.SupersededReason);
        Assert.Equal(AttributionState.Attributed, originalAttribution.State); // untouched otherwise

        Assert.True(call.IsFinal);
        Assert.Equal(88, call.ConnectedDurationSeconds);
    }

    [Fact]
    public async Task ChangedRecord_WithNoPriorAttribution_StillDecidesAFreshOne_WithoutError()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var record = InProgressRecord(startedAt);
        var call = Call.Create(
            record.SourceRecordId, record.Direction, record.DialledNumber, record.CallerId, record.StartedAt,
            record.AnsweredAt, record.EndedAt, record.ConnectedDurationSeconds, record.Disposition, record.IsFinal,
            ingestedAt: startedAt);

        var (service, calls, attributions) = BuildService();
        calls.Calls.Add(call);

        var completedRecord = record with { EndedAt = startedAt.AddSeconds(30), ConnectedDurationSeconds = 28, Disposition = "answered", IsFinal = true };

        var result = await service.ReDeriveIfChangedAsync(call, completedRecord, DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        Assert.Equal(AttributionState.Unattributed, result!.State); // no tracking number seeded — never allocated
        Assert.Single(attributions.Attributions);
    }
}
