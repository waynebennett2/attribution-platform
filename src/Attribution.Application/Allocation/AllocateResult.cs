namespace Attribution.Application.Allocation;

// FR-011: reason is populated whenever no session/allocation was created — consent
// withheld, no pool configured for the website, or the pool is exhausted — so the
// failure is recorded for operational visibility even though the visitor still sees a
// (default) number.
//
// FR-050: Pools and Allocations are populated only for a multi-pool-enabled website (null
// otherwise, so a single-pool website's JSON response carries neither field at all — see
// DniController). Number and ExpiresAt are the single-pool shape's fields; a multi-pool
// response instead carries its numbers inside Allocations, one per pool, and leaves Number
// null (per-pool numbers only, dni-api.md's multi-pool response shapes never repeat one at
// the top level).
public sealed record AllocateResult(
    Guid? SessionId,
    string? Number,
    string? Reason,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<PoolNumber>? Pools = null,
    IReadOnlyList<PoolAllocation>? Allocations = null);

// FR-050: one pool's id and its own default (matching) number — static metadata, not an
// allocation, safe to return before consent (spec.md FR-039 governs session/allocation
// creation and identifier storage, not this).
public sealed record PoolNumber(Guid PoolId, string DefaultNumber);

// FR-050: one pool's newly allocated tracking number within a multi-pool /allocate call.
public sealed record PoolAllocation(Guid PoolId, string Number, DateTimeOffset ExpiresAt);
