using System;
using Attribution.Domain.Pools;
using Xunit;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;

namespace Attribution.UnitTests.Allocation;

// FR-003, FR-005, FR-006: the eligibility rule that governs which numbers the atomic
// allocation query (research.md §2, integration-tested separately) is allowed to pick.
public class AllocationServiceTests
{
    private static readonly Guid PoolId = Guid.NewGuid();
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);

    [Fact]
    public void NeverAllocated_ActiveNumber_IsEligible()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000001");

        Assert.True(number.IsEligibleForAllocation(DateTimeOffset.UtcNow, Cooldown));
    }

    [Fact]
    public void SuspendedNumber_IsNeverEligible()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000002");
        number.Suspend();

        Assert.False(number.IsEligibleForAllocation(DateTimeOffset.UtcNow, Cooldown));
    }

    [Fact]
    public void RetiredNumber_IsNeverEligible()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000003");
        number.Retire();

        Assert.False(number.IsEligibleForAllocation(DateTimeOffset.UtcNow, Cooldown));
    }

    [Fact]
    public void ReleasedNumber_IsNotEligible_BeforeCooldownElapses()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000004");
        var releasedAt = DateTimeOffset.UtcNow;
        number.Release(releasedAt);

        var justBeforeCooldownEnds = releasedAt.Add(Cooldown).AddSeconds(-1);

        Assert.False(number.IsEligibleForAllocation(justBeforeCooldownEnds, Cooldown));
    }

    [Fact]
    public void ReleasedNumber_IsEligible_OnceCooldownElapses()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000005");
        var releasedAt = DateTimeOffset.UtcNow;
        number.Release(releasedAt);

        var atCooldownEnd = releasedAt.Add(Cooldown);

        Assert.True(number.IsEligibleForAllocation(atCooldownEnd, Cooldown));
    }

    [Fact]
    public void ReactivatedNumber_IsEligibleAgain()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000006");
        number.Suspend();
        number.Reactivate();

        Assert.True(number.IsEligibleForAllocation(DateTimeOffset.UtcNow, Cooldown));
    }

    [Fact]
    public void MovingToAnotherPool_ChangesPoolId_ButNotEligibility()
    {
        var number = TrackingNumber.Create(PoolId, "+15550000007");
        var newPoolId = Guid.NewGuid();

        number.MoveToPool(newPoolId);

        Assert.Equal(newPoolId, number.PoolId);
        Assert.True(number.IsEligibleForAllocation(DateTimeOffset.UtcNow, Cooldown));
    }
}

// FR-003, FR-018: the allocation window itself — computed extent, the withdrawal
// exception, and the overlap check the cooldown (FR-006) exists to prevent.
public class AllocationWindowTests
{
    private static readonly TimeSpan Extension = TimeSpan.FromMinutes(30);

    [Fact]
    public void Create_SetsWindowEnd_ToSessionExpiryPlusExtension()
    {
        var start = DateTimeOffset.UtcNow;
        var sessionExpiresAt = start.AddMinutes(30);

        var allocation = DomainAllocation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, sessionExpiresAt, Extension);

        Assert.Equal(sessionExpiresAt.Add(Extension), allocation.WindowEnd);
    }

    [Fact]
    public void CloseImmediately_EndsWindowAtWithdrawalMoment_NoExtension()
    {
        var start = DateTimeOffset.UtcNow;
        var allocation = DomainAllocation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension);

        var withdrawnAt = start.AddMinutes(5);
        allocation.CloseImmediately(withdrawnAt);

        Assert.Equal(withdrawnAt, allocation.WindowEnd);
        Assert.False(allocation.CoversInstant(withdrawnAt.AddSeconds(1)));
    }

    [Fact]
    public void TwoAllocations_ForDifferentNumbers_NeverOverlap_EvenWithSameWindow()
    {
        var start = DateTimeOffset.UtcNow;
        var a = DomainAllocation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension);
        var b = DomainAllocation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension);

        Assert.False(a.OverlapsWith(b));
    }

    [Fact]
    public void TwoAllocations_ForSameNumber_WithOverlappingWindows_DoOverlap()
    {
        var numberId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        var a = DomainAllocation.Create(numberId, Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension);
        var b = DomainAllocation.Create(numberId, Guid.NewGuid(), Guid.NewGuid(), start.AddMinutes(10), start.AddMinutes(40), Extension);

        Assert.True(a.OverlapsWith(b));
    }

    [Fact]
    public void TwoAllocations_ForSameNumber_SequentialAfterCooldown_DoNotOverlap()
    {
        var numberId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        var a = DomainAllocation.Create(numberId, Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(30), Extension);
        // b starts exactly when a's window ends — the FR-006 cooldown boundary.
        var b = DomainAllocation.Create(numberId, Guid.NewGuid(), Guid.NewGuid(), a.WindowEnd, a.WindowEnd.AddMinutes(30), Extension);

        Assert.False(a.OverlapsWith(b));
    }
}
