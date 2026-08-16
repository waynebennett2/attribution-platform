using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Attribution;

// FR-018, FR-019, FR-020, FR-021: exact DID + allocation-window matching — the core
// value proposition versus Mediahawk, and what the 95% accuracy target (SC) measures.
// Never probabilistic, fuzzy or heuristic: exactly one covering allocation window
// attributes, zero is unattributed, more than one is ambiguous and goes to review.
public sealed class AttributionService
{
    private readonly ITrackingNumberRepository _trackingNumberRepository;
    private readonly IAllocationRepository _allocationRepository;
    private readonly IAttributionRepository _attributionRepository;
    private readonly IReviewCaseRepository _reviewCaseRepository;

    public AttributionService(
        ITrackingNumberRepository trackingNumberRepository,
        IAllocationRepository allocationRepository,
        IAttributionRepository attributionRepository,
        IReviewCaseRepository reviewCaseRepository)
    {
        _trackingNumberRepository = trackingNumberRepository;
        _allocationRepository = allocationRepository;
        _attributionRepository = attributionRepository;
        _reviewCaseRepository = reviewCaseRepository;
    }

    // FR-018's matching rule, isolated as pure logic (no repository access) so it can be
    // unit-tested directly against hand-built allocation lists — the full pipeline below
    // (resolving the dialled number, fetching allocations, persisting the result) is
    // covered separately by the integration test against a real database
    // (SeededCallAttributionTests, SC-001's full scenario set).
    public static AttributionDecision Decide(IReadOnlyList<DomainAllocation> coveringAllocations)
    {
        if (coveringAllocations.Count == 0)
        {
            // FR-020: no allocation window covered the call at its start time.
            return new AttributionDecision(AttributionState.Unattributed, null, null, "no_allocation_window_covers_call_start");
        }

        if (coveringAllocations.Count > 1)
        {
            // FR-021: more than one window matches — never guessed between them.
            // FR-049: an overlap where every covering window is shadow-derived is the
            // parallel run's expected shape, not a defect signal — reported under a
            // distinct reason so it stays separable from ordinary-operation ambiguity.
            var allShadow = coveringAllocations.All(a => a.IsShadow);
            var reason = allShadow ? "shadow_mode_overlapping_observed_windows" : "multiple_allocation_windows_cover_call_start";
            return new AttributionDecision(AttributionState.Ambiguous, null, null, reason, IsShadowDerived: allShadow);
        }

        var allocation = coveringAllocations[0];
        return new AttributionDecision(
            AttributionState.Attributed, allocation.SessionId, allocation.Id, null, IsShadowDerived: allocation.IsShadow);
    }

    // Orchestrates the full pipeline for one call: resolve the dialled number to a
    // tracking number, fetch whichever allocation(s) covered it at the call's start time
    // (FR-018's core lookup), apply Decide() above, and persist the result as the
    // evidence FR-019 requires.
    public async Task<DomainAttribution> AttributeAsync(Call call, DateTimeOffset decidedAt)
    {
        var trackingNumber = await _trackingNumberRepository.GetByDidAsync(call.DialledNumber);
        if (trackingNumber is null)
        {
            // spec.md edge case: a call to a tracking number the platform has no record
            // of ever allocating.
            var neverAllocated = DomainAttribution.Unattributed(call.Id, "number_never_allocated", decidedAt);
            await _attributionRepository.AddAsync(neverAllocated);
            return neverAllocated;
        }

        var coveringAllocations = await _allocationRepository.GetCoveringInstantAsync(trackingNumber.Id, call.StartedAt);
        var decision = Decide(coveringAllocations);

        var attribution = decision.State switch
        {
            AttributionState.Attributed => DomainAttribution.Attributed(
                call.Id, decision.SessionId!.Value, decision.AllocationId!.Value, decidedAt, decision.IsShadowDerived),
            AttributionState.Ambiguous => DomainAttribution.Ambiguous(call.Id, decision.Reason!, decidedAt, decision.IsShadowDerived),
            _ => DomainAttribution.Unattributed(call.Id, decision.Reason!, decidedAt),
        };

        await _attributionRepository.AddAsync(attribution);

        if (attribution.State == AttributionState.Ambiguous)
        {
            // FR-021, FR-036: never guessed between competing windows — raised for
            // manual review instead.
            var reviewCase = ReviewCase.Open(call.Id, attribution.Id, decidedAt);
            await _reviewCaseRepository.AddAsync(reviewCase);
        }

        return attribution;
    }
}
