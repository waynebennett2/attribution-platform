namespace Attribution.Domain.Audit;

// FR-021, FR-036: raised whenever a call's attribution is decided ambiguous, so a human
// resolves which session (if any) actually generated it. Resolution (T096, User Story 6)
// supersedes the ambiguous Attribution row rather than editing it, so the original
// decision stays as permanent history alongside the reason it was overridden.
public class ReviewCase
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CallId { get; private set; }
    public Guid AttributionId { get; private set; }
    public ReviewCaseStatus Status { get; private set; } = ReviewCaseStatus.Open;
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? AgeAlertRaisedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? Resolution { get; private set; }

    private ReviewCase() { }

    public static ReviewCase Open(Guid callId, Guid attributionId, DateTimeOffset openedAt) =>
        new() { CallId = callId, AttributionId = attributionId, OpenedAt = openedAt };

    internal static ReviewCase Rehydrate(
        Guid id, Guid callId, Guid attributionId, ReviewCaseStatus status, DateTimeOffset openedAt,
        DateTimeOffset? ageAlertRaisedAt, string? resolvedBy, DateTimeOffset? resolvedAt, string? resolution) => new()
        {
            Id = id,
            CallId = callId,
            AttributionId = attributionId,
            Status = status,
            OpenedAt = openedAt,
            AgeAlertRaisedAt = ageAlertRaisedAt,
            ResolvedBy = resolvedBy,
            ResolvedAt = resolvedAt,
            Resolution = resolution,
        };
}
