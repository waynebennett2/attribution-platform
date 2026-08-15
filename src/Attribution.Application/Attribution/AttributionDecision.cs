using Attribution.Domain.Calls;

namespace Attribution.Application.Attribution;

// The outcome of applying FR-018's matching rule to one call's covering allocations,
// before it's turned into a persisted Domain.Calls.Attribution row.
public sealed record AttributionDecision(AttributionState State, Guid? SessionId, Guid? AllocationId, string? Reason);
