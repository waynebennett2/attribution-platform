namespace Attribution.Application.Allocation;

// FR-012: the outcome of one heartbeat call. Allocations is populated only for a
// multi-pool-enabled website's session (null otherwise, so a single-pool website's JSON
// response is unaffected — see DniController); StillValid/Number remain the single-pool
// shape's fields, left null on a multi-pool response since per-pool validity/numbers live
// inside Allocations instead (dni-api.md).
public sealed record HeartbeatResult(
    bool StillValid,
    string? Number,
    IReadOnlyList<PoolHeartbeat>? Allocations = null);

// FR-050: one pool's current validity and number within a multi-pool session's heartbeat.
public sealed record PoolHeartbeat(Guid PoolId, bool StillValid, string? Number);
