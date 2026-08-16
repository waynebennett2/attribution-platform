using System;
using Attribution.Application.Attribution;
using Attribution.Application.Ingestion;
using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Attribution.UnitTests.TestSupport;
using Xunit;

namespace Attribution.UnitTests.Ingestion;

// FR-017: repeated ingestion of the same source data must produce no duplicate Call, no
// duplicate Call Leg and no duplicate attribution. FR-016: the checkpoint only advances
// once a page's records are durably upserted, and a page with nothing new leaves it
// exactly where it was.
public class IngestionTests
{
    private const string Feed = "8x8-cdr";

    private static (IngestionService Service, FakeCallRepository Calls, FakeCallLegRepository Legs,
        FakeIngestionCheckpointRepository Checkpoints, FakeAttributionRepository Attributions) BuildService()
    {
        var calls = new FakeCallRepository();
        var legs = new FakeCallLegRepository();
        var checkpoints = new FakeIngestionCheckpointRepository();
        var attributions = new FakeAttributionRepository();
        var attributionService = new AttributionService(
            new FakeTrackingNumberRepository(), new FakeAllocationRepository(), attributions, new FakeReviewCaseRepository());

        var rules = new FakeQualificationRuleRepository();
        rules.Rules.Add(QualificationRule.Create(
            QualificationScopeType.Default, null, 1, QualificationConditions.Default,
            DateTimeOffset.UtcNow.AddYears(-1), null, "seed", DateTimeOffset.UtcNow.AddYears(-1)));
        var qualificationService = new QualificationService(
            rules, new FakeQualificationResultRepository(), new FakeSessionRepository(), new FakeWebsiteRepository());

        var reDerivationService = new ReDerivationService(
            calls, attributions, new FakeQualificationResultRepository(), attributionService, qualificationService);
        var service = new IngestionService(calls, legs, checkpoints, attributionService, reDerivationService, qualificationService);
        return (service, calls, legs, checkpoints, attributions);
    }

    private static Analytics8x8CallRecord CallRecord(string sourceRecordId, DateTimeOffset startedAt, bool isFinal = true) => new(
        SourceRecordId: sourceRecordId,
        Direction: CallDirection.Inbound,
        DialledNumber: "+441632960001",
        CallerId: "+441632960999",
        StartedAt: startedAt,
        AnsweredAt: startedAt.AddSeconds(2),
        EndedAt: isFinal ? startedAt.AddSeconds(60) : null,
        ConnectedDurationSeconds: isFinal ? 58 : null,
        Disposition: isFinal ? "answered" : null,
        IsFinal: isFinal);

    [Fact]
    public async Task NewCall_IsUpserted_AndAttributedOnce()
    {
        var (service, calls, _, _, attributions) = BuildService();
        var startedAt = DateTimeOffset.UtcNow;
        var page = new Analytics8x8Page(new[] { CallRecord("cdr-1", startedAt) }, Array.Empty<Analytics8x8CallLegRecord>(), "pos-1");

        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);

        Assert.Single(calls.Calls);
        Assert.Single(attributions.Attributions);
    }

    [Fact]
    public async Task ReprocessingAnIdenticalPageThreeTimes_ProducesNoDuplicateCallOrAttribution()
    {
        var (service, calls, _, _, attributions) = BuildService();
        var startedAt = DateTimeOffset.UtcNow;
        var page = new Analytics8x8Page(new[] { CallRecord("cdr-1", startedAt) }, Array.Empty<Analytics8x8CallLegRecord>(), "pos-1");

        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);
        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);
        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);

        Assert.Single(calls.Calls);
        Assert.Single(attributions.Attributions); // no re-derivation triggered — nothing changed
    }

    [Fact]
    public async Task CallLeg_ArrivingBeforeItsCall_IsOrphaned_ThenLinkedOnceTheCallArrives()
    {
        var (service, calls, legs, _, _) = BuildService();
        var startedAt = DateTimeOffset.UtcNow;
        var legPage = new Analytics8x8Page(
            Array.Empty<Analytics8x8CallRecord>(),
            new[] { new Analytics8x8CallLegRecord("cdr-1", "leg-1", "primary", startedAt, startedAt.AddSeconds(60)) },
            "pos-1");

        await service.ProcessPageAsync(Feed, legPage, DateTimeOffset.UtcNow);

        var orphan = Assert.Single(legs.Legs);
        Assert.Null(orphan.CallId);

        var callPage = new Analytics8x8Page(new[] { CallRecord("cdr-1", startedAt) }, Array.Empty<Analytics8x8CallLegRecord>(), "pos-2");
        await service.ProcessPageAsync(Feed, callPage, DateTimeOffset.UtcNow);

        Assert.Equal(calls.Calls[0].Id, legs.Legs[0].CallId);
    }

    [Fact]
    public async Task ReprocessingAnIdenticalCallLeg_DoesNotDuplicate()
    {
        var (service, _, legs, _, _) = BuildService();
        var startedAt = DateTimeOffset.UtcNow;
        var page = new Analytics8x8Page(
            Array.Empty<Analytics8x8CallRecord>(),
            new[] { new Analytics8x8CallLegRecord("cdr-1", "leg-1", "primary", startedAt, startedAt.AddSeconds(60)) },
            "pos-1");

        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);
        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);

        Assert.Single(legs.Legs);
    }

    [Fact]
    public async Task FirstPage_CreatesTheCheckpoint_AtThePagesNextPosition()
    {
        var (service, _, _, checkpoints, _) = BuildService();
        var page = new Analytics8x8Page(Array.Empty<Analytics8x8CallRecord>(), Array.Empty<Analytics8x8CallLegRecord>(), "pos-1");

        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);

        var checkpoint = Assert.Single(checkpoints.Checkpoints);
        Assert.Equal(Feed, checkpoint.Feed);
        Assert.Equal("pos-1", checkpoint.Position);
    }

    [Fact]
    public async Task SubsequentPage_AdvancesTheExistingCheckpoint()
    {
        var (service, _, _, checkpoints, _) = BuildService();
        var firstPage = new Analytics8x8Page(Array.Empty<Analytics8x8CallRecord>(), Array.Empty<Analytics8x8CallLegRecord>(), "pos-1");
        var secondPage = new Analytics8x8Page(Array.Empty<Analytics8x8CallRecord>(), Array.Empty<Analytics8x8CallLegRecord>(), "pos-2");

        await service.ProcessPageAsync(Feed, firstPage, DateTimeOffset.UtcNow);
        await service.ProcessPageAsync(Feed, secondPage, DateTimeOffset.UtcNow);

        var checkpoint = Assert.Single(checkpoints.Checkpoints);
        Assert.Equal("pos-2", checkpoint.Position);
    }

    [Fact]
    public async Task PageWithNoNextPosition_LeavesTheCheckpointUntouched()
    {
        // BackfillService's use of the pipeline: PollRangeAsync's page never carries a
        // NextCheckpointPosition, so a backfill can never disturb the live checkpoint.
        var (service, _, _, checkpoints, _) = BuildService();
        var page = new Analytics8x8Page(Array.Empty<Analytics8x8CallRecord>(), Array.Empty<Analytics8x8CallLegRecord>(), NextCheckpointPosition: null);

        await service.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);

        Assert.Empty(checkpoints.Checkpoints);
    }
}
