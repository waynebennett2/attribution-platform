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

    // FR-050: allocates one additional pool's number onto a Session that is already
    // persisted — a multi-pool session's second-and-later pool at creation time (once the
    // first pool's TryAllocateAsync call has already inserted the Visitor and Session), or
    // growing an already-active session on a later page view (research.md §15). Same
    // per-pool atomic SKIP LOCKED pick as TryAllocateAsync, without inserting a Visitor or
    // Session that already exists.
    Task<AllocationAttemptResult> TryAllocateAdditionalAsync(
        Session session,
        Guid poolId,
        TimeSpan cooldown,
        DateTimeOffset windowStart,
        TimeSpan allocationWindowExtension,
        DateTimeOffset now);
}
