namespace Attribution.Domain.Audit;

// FR-047: one open Alert row per (ConditionType, ScopeRef) while a condition is firing —
// a repeat notification updates LastNotifiedAt on this same row rather than creating a
// new Alert, so a sustained outage produces a continuing alert, never a flood.
public class Alert
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public AlertConditionType ConditionType { get; private set; }
    public string? ScopeRef { get; private set; }
    public string Threshold { get; private set; } = string.Empty;
    public DateTimeOffset RaisedAt { get; private set; }
    public DateTimeOffset LastNotifiedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public DateTimeOffset? ClearedAt { get; private set; }

    public bool IsOpen => ClearedAt is null;

    private Alert() { }

    public static Alert Raise(AlertConditionType conditionType, string? scopeRef, string threshold, DateTimeOffset raisedAt) =>
        new()
        {
            ConditionType = conditionType,
            ScopeRef = scopeRef,
            Threshold = threshold,
            RaisedAt = raisedAt,
            LastNotifiedAt = raisedAt,
        };

    // FR-047: repeat notification for a condition already firing — same row, not a new alert.
    public void RecordRepeatNotification(DateTimeOffset notifiedAt) => LastNotifiedAt = notifiedAt;

    // Stops repeat notification but does not clear the underlying condition — only the
    // next healthy evaluation does that (Clear()).
    public void Acknowledge(string acknowledgedBy, DateTimeOffset acknowledgedAt)
    {
        AcknowledgedAt = acknowledgedAt;
        AcknowledgedBy = acknowledgedBy;
    }

    public void Clear(DateTimeOffset clearedAt) => ClearedAt = clearedAt;

    internal static Alert Rehydrate(
        Guid id, AlertConditionType conditionType, string? scopeRef, string threshold, DateTimeOffset raisedAt,
        DateTimeOffset lastNotifiedAt, DateTimeOffset? acknowledgedAt, string? acknowledgedBy, DateTimeOffset? clearedAt) => new()
        {
            Id = id,
            ConditionType = conditionType,
            ScopeRef = scopeRef,
            Threshold = threshold,
            RaisedAt = raisedAt,
            LastNotifiedAt = lastNotifiedAt,
            AcknowledgedAt = acknowledgedAt,
            AcknowledgedBy = acknowledgedBy,
            ClearedAt = clearedAt,
        };
}
