using System;
using System.Collections.Generic;
using Attribution.Application.Attribution;
using Attribution.Domain.Calls;
using Xunit;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.UnitTests.Attribution;

// FR-018, FR-020, FR-021: the strict DID + allocation-window matching rule, isolated as
// pure logic (AttributionService.Decide) so it's tested directly against hand-built
// allocation lists — no database. The full pipeline (resolving the dialled number,
// fetching allocations, persisting the result) is covered separately by the integration
// test against a real database (SeededCallAttributionTests, SC-001's full scenario set).
public class AttributionServiceTests
{
    private static readonly TimeSpan Extension = TimeSpan.FromMinutes(30);

    private static DomainAllocation CoveringAllocation(DateTimeOffset callStart) =>
        DomainAllocation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            windowStart: callStart.AddMinutes(-5), sessionExpiresAt: callStart.AddMinutes(5), Extension);

    [Fact]
    public void NoCoveringAllocations_IsUnattributed()
    {
        var decision = AttributionService.Decide(Array.Empty<DomainAllocation>());

        Assert.Equal(AttributionState.Unattributed, decision.State);
        Assert.Null(decision.SessionId);
        Assert.Null(decision.AllocationId);
        Assert.Equal("no_allocation_window_covers_call_start", decision.Reason);
    }

    [Fact]
    public void ExactlyOneCoveringAllocation_IsAttributed_ToThatAllocationsSession()
    {
        var callStart = DateTimeOffset.UtcNow;
        var allocation = CoveringAllocation(callStart);

        var decision = AttributionService.Decide(new List<DomainAllocation> { allocation });

        Assert.Equal(AttributionState.Attributed, decision.State);
        Assert.Equal(allocation.SessionId, decision.SessionId);
        Assert.Equal(allocation.Id, decision.AllocationId);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void MoreThanOneCoveringAllocation_IsAmbiguous_AndAssignsNoSession()
    {
        var callStart = DateTimeOffset.UtcNow;
        var first = CoveringAllocation(callStart);
        var second = CoveringAllocation(callStart);

        var decision = AttributionService.Decide(new List<DomainAllocation> { first, second });

        Assert.Equal(AttributionState.Ambiguous, decision.State);
        Assert.Null(decision.SessionId);
        Assert.Null(decision.AllocationId);
        Assert.Equal("multiple_allocation_windows_cover_call_start", decision.Reason);
    }

    [Fact]
    public void ThreeOrMoreCoveringAllocations_AreStillJustAmbiguous_NeverAGuessedBest()
    {
        var callStart = DateTimeOffset.UtcNow;
        var allocations = new List<DomainAllocation>
        {
            CoveringAllocation(callStart), CoveringAllocation(callStart), CoveringAllocation(callStart),
        };

        var decision = AttributionService.Decide(allocations);

        Assert.Equal(AttributionState.Ambiguous, decision.State);
    }
}

// FR-045: re-derivation must supersede a decided row rather than overwrite it, keeping
// the prior decision as permanent history.
public class AttributionSupersedeTests
{
    [Fact]
    public void Supersede_MarksNotCurrent_AndRecordsReason_WithoutChangingTheOriginalDecision()
    {
        var callId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var decidedAt = DateTimeOffset.UtcNow;

        var attribution = DomainAttribution.Attributed(callId, sessionId, allocationId, decidedAt);

        attribution.Supersede("call_record_restated");

        Assert.False(attribution.IsCurrent);
        Assert.Equal("call_record_restated", attribution.SupersededReason);
        // The original decision itself is untouched — only the current-ness flag changed.
        Assert.Equal(AttributionState.Attributed, attribution.State);
        Assert.Equal(sessionId, attribution.SessionId);
        Assert.Equal(allocationId, attribution.AllocationId);
        Assert.Equal(decidedAt, attribution.DecidedAt);
    }
}
