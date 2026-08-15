namespace Attribution.Domain.Calls;

// Key Entities §Call Leg: a constituent segment of a call, used to reconstruct the call
// journey. Spec.md "Ingestion resilience" names call legs arriving before the Call
// Detail Record they belong to as an expected case, so CallId starts unset and is
// linked once the parent CDR exists rather than requiring a strict arrival order.
public class CallLeg
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? CallId { get; private set; }
    public string SourceCallRecordId { get; private set; } = string.Empty;
    public string SourceLegId { get; private set; } = string.Empty;
    public string? SequenceOrRole { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    private CallLeg() { }

    public static CallLeg Create(
        string sourceCallRecordId,
        string sourceLegId,
        string? sequenceOrRole,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt,
        Guid? callId = null)
    {
        return new CallLeg
        {
            CallId = callId,
            SourceCallRecordId = sourceCallRecordId,
            SourceLegId = sourceLegId,
            SequenceOrRole = sequenceOrRole,
            StartedAt = startedAt,
            EndedAt = endedAt,
        };
    }

    // Called once the parent CDR is ingested for a leg that arrived first (orphaned leg).
    public void LinkToCall(Guid callId) => CallId = callId;

    internal static CallLeg Rehydrate(
        Guid id, Guid? callId, string sourceCallRecordId, string sourceLegId,
        string? sequenceOrRole, DateTimeOffset? startedAt, DateTimeOffset? endedAt) => new()
        {
            Id = id,
            CallId = callId,
            SourceCallRecordId = sourceCallRecordId,
            SourceLegId = sourceLegId,
            SequenceOrRole = sequenceOrRole,
            StartedAt = startedAt,
            EndedAt = endedAt,
        };
}
