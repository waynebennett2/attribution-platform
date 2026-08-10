namespace Attribution.Application.Allocation;

// FR-011: reason is populated whenever no session/allocation was created — consent
// withheld, no pool configured for the website, or the pool is exhausted — so the
// failure is recorded for operational visibility even though the visitor still sees a
// (default) number.
public sealed record AllocateResult(Guid? SessionId, string Number, string? Reason, DateTimeOffset? ExpiresAt);
