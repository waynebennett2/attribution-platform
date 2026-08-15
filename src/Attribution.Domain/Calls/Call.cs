namespace Attribution.Domain.Calls;

// FR-017: sourced from 8x8, ingested idempotently on its own record identifier
// (source_record_id) so repeated ingestion of the same source data produces no
// duplicate records.
public class Call
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string SourceRecordId { get; private set; } = string.Empty;
    public CallDirection Direction { get; private set; }
    public string DialledNumber { get; private set; } = string.Empty;
    public string? CallerId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public int? ConnectedDurationSeconds { get; private set; }
    public string? Disposition { get; private set; }
    public bool IsFinal { get; private set; }
    public DateTimeOffset IngestedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Call() { }

    public static Call Create(
        string sourceRecordId,
        CallDirection direction,
        string dialledNumber,
        string? callerId,
        DateTimeOffset startedAt,
        DateTimeOffset? answeredAt,
        DateTimeOffset? endedAt,
        int? connectedDurationSeconds,
        string? disposition,
        bool isFinal,
        DateTimeOffset ingestedAt)
    {
        return new Call
        {
            SourceRecordId = sourceRecordId,
            Direction = direction,
            DialledNumber = dialledNumber,
            CallerId = callerId,
            StartedAt = startedAt,
            AnsweredAt = answeredAt,
            EndedAt = endedAt,
            ConnectedDurationSeconds = connectedDurationSeconds,
            Disposition = disposition,
            IsFinal = isFinal,
            IngestedAt = ingestedAt,
            UpdatedAt = ingestedAt,
        };
    }

    // FR-045: 8x8 supplies a changed version of an already-processed record — a call
    // that was in progress at the previous ingestion, or a later correction. Returns
    // whether any fact actually changed, so the caller can skip re-derivation entirely
    // on a genuinely unchanged re-ingestion (FR-045's idempotence requirement) rather
    // than re-deciding attribution/qualification for no reason.
    public bool ApplyRestatement(
        DateTimeOffset? answeredAt,
        DateTimeOffset? endedAt,
        int? connectedDurationSeconds,
        string? disposition,
        bool isFinal,
        DateTimeOffset updatedAt)
    {
        var changed = AnsweredAt != answeredAt
            || EndedAt != endedAt
            || ConnectedDurationSeconds != connectedDurationSeconds
            || Disposition != disposition
            || IsFinal != isFinal;

        if (!changed)
        {
            return false;
        }

        AnsweredAt = answeredAt;
        EndedAt = endedAt;
        ConnectedDurationSeconds = connectedDurationSeconds;
        Disposition = disposition;
        IsFinal = isFinal;
        UpdatedAt = updatedAt;
        return true;
    }

    internal static Call Rehydrate(
        Guid id, string sourceRecordId, CallDirection direction, string dialledNumber, string? callerId,
        DateTimeOffset startedAt, DateTimeOffset? answeredAt, DateTimeOffset? endedAt,
        int? connectedDurationSeconds, string? disposition, bool isFinal,
        DateTimeOffset ingestedAt, DateTimeOffset updatedAt) => new()
        {
            Id = id,
            SourceRecordId = sourceRecordId,
            Direction = direction,
            DialledNumber = dialledNumber,
            CallerId = callerId,
            StartedAt = startedAt,
            AnsweredAt = answeredAt,
            EndedAt = endedAt,
            ConnectedDurationSeconds = connectedDurationSeconds,
            Disposition = disposition,
            IsFinal = isFinal,
            IngestedAt = ingestedAt,
            UpdatedAt = updatedAt,
        };
}
