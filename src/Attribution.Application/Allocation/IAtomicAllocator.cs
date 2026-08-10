using Attribution.Domain.Sessions;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;

namespace Attribution.Application.Allocation;

public sealed record AllocationAttemptResult(bool Succeeded, DomainAllocation? Allocation);

// FR-003, FR-011: atomically persists a new Visitor and Session and reserves one
// eligible tracking number from the pool, all in a single database transaction — so a
// pool-exhausted failure leaves nothing persisted (mirroring Acceptance Scenario 6's
// no-consent case: no session record on failure) rather than an orphaned Session with no
// Allocation. Deliberately not a Domain repository interface: it's a use-case-specific
// atomic operation whose correctness depends on Infrastructure's transaction/locking
// mechanics (research.md §2), defined where it's consumed (Application) and implemented
// where the database lives (Infrastructure).
public interface IAtomicAllocator
{
    Task<AllocationAttemptResult> TryAllocateAsync(
        Visitor visitor,
        Session session,
        Guid poolId,
        TimeSpan cooldown,
        DateTimeOffset windowStart,
        TimeSpan allocationWindowExtension,
        DateTimeOffset now);
}
