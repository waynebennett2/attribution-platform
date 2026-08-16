namespace Attribution.Domain.Qualification;

// FR-022, FR-024, FR-045: the outcome of judging one attributed call against whichever
// rule version and scope were in force at its start time. A rule change never mutates an
// existing is_current=true row (FR-024) — only a source restatement (FR-045) or a manual
// review resolution that changes attribution can supersede one, and even then the
// superseded row is retained as permanent history alongside the reason.
public class QualificationResult
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CallId { get; private set; }
    public Guid AttributionId { get; private set; }
    public Guid QualificationRuleId { get; private set; }
    public bool IsQualified { get; private set; }
    public bool IsCurrent { get; private set; } = true;
    public string? SupersededReason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; } = DateTimeOffset.UtcNow;

    private QualificationResult() { }

    public static QualificationResult Decide(
        Guid callId, Guid attributionId, Guid qualificationRuleId, bool isQualified, DateTimeOffset decidedAt) =>
        new()
        {
            CallId = callId,
            AttributionId = attributionId,
            QualificationRuleId = qualificationRuleId,
            IsQualified = isQualified,
            DecidedAt = decidedAt,
        };

    public void Supersede(string reason)
    {
        IsCurrent = false;
        SupersededReason = reason;
    }

    internal static QualificationResult Rehydrate(
        Guid id, Guid callId, Guid attributionId, Guid qualificationRuleId, bool isQualified, bool isCurrent,
        string? supersededReason, DateTimeOffset decidedAt) => new()
        {
            Id = id,
            CallId = callId,
            AttributionId = attributionId,
            QualificationRuleId = qualificationRuleId,
            IsQualified = isQualified,
            IsCurrent = isCurrent,
            SupersededReason = supersededReason,
            DecidedAt = decidedAt,
        };
}
