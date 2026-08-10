namespace Attribution.Domain.Pools;

// FR-004, FR-005, FR-006: an 8x8 DID belonging to exactly one pool at a time.
public class TrackingNumber
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PoolId { get; private set; }
    public string Did { get; private set; } = string.Empty;
    public TrackingNumberStatus Status { get; private set; } = TrackingNumberStatus.Active;
    public DateTimeOffset StatusChangedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastReleasedAt { get; private set; }

    private TrackingNumber() { }

    public static TrackingNumber Create(Guid poolId, string did)
    {
        if (string.IsNullOrWhiteSpace(did))
        {
            throw new ArgumentException("A tracking number must have a DID.", nameof(did));
        }

        return new TrackingNumber { PoolId = poolId, Did = did };
    }

    // FR-006: a number is eligible for allocation only if it's active, and — if it has
    // been allocated before — the cooldown since its last release has fully elapsed.
    // This is the pure eligibility rule, useful for a quick/optimistic check (e.g. an
    // admin "is this number currently allocatable" view). LastReleasedAt is a denormalized
    // hint kept roughly in sync by AllocationService when a window closes; it is NOT the
    // authority the atomic allocation query relies on for correctness — that query checks
    // the allocations table directly within its FOR UPDATE SKIP LOCKED transaction
    // (research.md §2), because only that check is safe under concurrency.
    public bool IsEligibleForAllocation(DateTimeOffset now, TimeSpan cooldown)
    {
        if (Status != TrackingNumberStatus.Active)
        {
            return false;
        }

        return LastReleasedAt is null || now >= LastReleasedAt.Value.Add(cooldown);
    }

    public void Release(DateTimeOffset releasedAt) => LastReleasedAt = releasedAt;

    // FR-005: suspension/retirement never touches an in-progress allocation — it only
    // changes the number's status, which IsEligibleForAllocation already checks first.
    public void Suspend()
    {
        Status = TrackingNumberStatus.Suspended;
        StatusChangedAt = DateTimeOffset.UtcNow;
    }

    public void Reactivate()
    {
        Status = TrackingNumberStatus.Active;
        StatusChangedAt = DateTimeOffset.UtcNow;
    }

    public void Retire()
    {
        Status = TrackingNumberStatus.Retired;
        StatusChangedAt = DateTimeOffset.UtcNow;
    }

    // FR-004: moving a number to a different pool removes it from the previous one;
    // historical allocations keep referencing the pool that held it at the time via
    // Allocation.PoolIdAtAllocation, which this method does not (and must not) touch.
    public void MoveToPool(Guid newPoolId) => PoolId = newPoolId;

    internal static TrackingNumber Rehydrate(
        Guid id, Guid poolId, string did, TrackingNumberStatus status,
        DateTimeOffset statusChangedAt, DateTimeOffset? lastReleasedAt) => new()
        {
            Id = id,
            PoolId = poolId,
            Did = did,
            Status = status,
            StatusChangedAt = statusChangedAt,
            LastReleasedAt = lastReleasedAt,
        };
}
