using System;
using Xunit;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;

namespace Attribution.UnitTests.ShadowMode;

// FR-049: shadow-mode allocations are flagged distinctly, and overlapping windows for
// the same observed number are tolerated (the cooldown belongs to the system doing the
// inserting, not to this platform) — a property of the parallel run, not a defect signal,
// unlike an ordinary-operation overlap (see AllocationWindowTests in this same phase).
public class ShadowModeTests
{
    private static readonly TimeSpan Extension = TimeSpan.FromMinutes(30);

    [Fact]
    public void Create_WithIsShadowTrue_IsFlaggedAsShadowDerived()
    {
        var start = DateTimeOffset.UtcNow;
        var allocation = DomainAllocation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension, isShadow: true);

        Assert.True(allocation.IsShadow);
    }

    [Fact]
    public void Create_WithoutIsShadow_DefaultsToOrdinary()
    {
        var start = DateTimeOffset.UtcNow;
        var allocation = DomainAllocation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension);

        Assert.False(allocation.IsShadow);
    }

    [Fact]
    public void TwoShadowAllocations_ForSameObservedNumber_CanOverlap_WithoutConstructionFailing()
    {
        // FR-049: because the re-use interval of an observed number is controlled by the
        // inserting system rather than by FR-006, overlapping shadow windows are a normal,
        // constructible outcome — classification as ambiguous (and reporting it separately
        // from ordinary ambiguity) is Attribution's job, not something the entity itself
        // needs to reject.
        var numberId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;

        var a = DomainAllocation.Create(numberId, Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(20), Extension, isShadow: true);
        var b = DomainAllocation.Create(numberId, Guid.NewGuid(), Guid.NewGuid(), start.AddMinutes(5), start.AddMinutes(25), Extension, isShadow: true);

        Assert.True(a.OverlapsWith(b));
        Assert.True(a.IsShadow);
        Assert.True(b.IsShadow);
    }

    [Fact]
    public void ShadowAllocation_StillComputesWindowEnd_FromSessionExpiryPlusExtension()
    {
        var start = DateTimeOffset.UtcNow;
        var sessionExpiresAt = start.AddMinutes(30);

        var allocation = DomainAllocation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, sessionExpiresAt, Extension, isShadow: true);

        // FR-049: shadow allocations are attributed by the identical strict rules of
        // FR-018 — no exception to window computation just because it's a shadow row.
        Assert.Equal(sessionExpiresAt.Add(Extension), allocation.WindowEnd);
    }
}
