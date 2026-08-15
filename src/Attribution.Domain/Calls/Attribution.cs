namespace Attribution.Domain.Calls;

// Key Entities §Attribution: the decision linking a call to a session, with state
// attributed/unattributed/ambiguous, the reason, and the stored supporting evidence
// (FR-019: the matched number, the matched session, the allocation window, the call
// start time and the rule applied — Session/Allocation/Call are reachable via the
// foreign keys here, so no evidence is duplicated onto this row). Re-derivation
// (FR-045) never mutates a decided row in place — it supersedes it and a new current
// row is inserted — so the prior decision is always retained as history.
public class Attribution
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CallId { get; private set; }
    public Guid? SessionId { get; private set; }
    public Guid? AllocationId { get; private set; }
    public AttributionState State { get; private set; }
    public string? Reason { get; private set; }
    public bool IsShadowDerived { get; private set; } // FR-049
    public bool IsCurrent { get; private set; } = true;
    public string? SupersededReason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Attribution() { }

    public static Attribution Attributed(
        Guid callId, Guid sessionId, Guid allocationId, DateTimeOffset decidedAt, bool isShadowDerived = false) =>
        new()
        {
            CallId = callId,
            SessionId = sessionId,
            AllocationId = allocationId,
            State = AttributionState.Attributed,
            IsShadowDerived = isShadowDerived,
            DecidedAt = decidedAt,
        };

    // FR-020: no allocation window covered the call at its start time.
    public static Attribution Unattributed(Guid callId, string reason, DateTimeOffset decidedAt) =>
        new()
        {
            CallId = callId,
            State = AttributionState.Unattributed,
            Reason = reason,
            DecidedAt = decidedAt,
        };

    // FR-021: more than one allocation window covered the call at its start time —
    // never guessed between them, always raised for human review instead.
    public static Attribution Ambiguous(Guid callId, string reason, DateTimeOffset decidedAt) =>
        new()
        {
            CallId = callId,
            State = AttributionState.Ambiguous,
            Reason = reason,
            DecidedAt = decidedAt,
        };

    // FR-045: marks this decision superseded when a source restatement produces a new
    // one. The row itself is never overwritten — only flagged not-current — so it
    // remains in place as permanent history alongside the reason for the change.
    public void Supersede(string reason)
    {
        IsCurrent = false;
        SupersededReason = reason;
    }

    internal static Attribution Rehydrate(
        Guid id, Guid callId, Guid? sessionId, Guid? allocationId, AttributionState state, string? reason,
        bool isShadowDerived, bool isCurrent, string? supersededReason, DateTimeOffset decidedAt) => new()
        {
            Id = id,
            CallId = callId,
            SessionId = sessionId,
            AllocationId = allocationId,
            State = state,
            Reason = reason,
            IsShadowDerived = isShadowDerived,
            IsCurrent = isCurrent,
            SupersededReason = supersededReason,
            DecidedAt = decidedAt,
        };
}
