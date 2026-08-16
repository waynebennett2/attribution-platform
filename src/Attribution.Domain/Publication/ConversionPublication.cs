namespace Attribution.Domain.Publication;

// FR-025-FR-028, FR-044: one attempt to report one qualified call to one destination —
// the outbox row research.md §3 describes. IdempotencyKey is stable for one publish
// episode (from a call becoming qualified through its publication and any correction);
// a genuine retract-then-requalify gets a fresh row with a fresh key (PublicationService
// only creates a new row when no non-retracted row already exists for the call+destination
// — see IConversionPublicationRepository.GetActiveForCallAsync).
public class ConversionPublication
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid QualificationResultId { get; private set; }
    public PublicationDestination Destination { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public PublicationStatus Status { get; private set; }
    public string? SkippedReason { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ExternalId { get; private set; }
    public string? LastError { get; private set; }
    public PublicationCorrection? Correction { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? CorrectedAt { get; private set; }

    private ConversionPublication() { }

    public static ConversionPublication CreatePending(
        Guid qualificationResultId, PublicationDestination destination, string idempotencyKey) => new()
        {
            QualificationResultId = qualificationResultId,
            Destination = destination,
            IdempotencyKey = idempotencyKey,
            Status = PublicationStatus.Pending,
        };

    // FR-026: no Google click identifier (Google Ads) or GA4 client id (GA4) on the
    // originating session — never eligible to publish, recorded once rather than retried.
    public static ConversionPublication CreateSkipped(
        Guid qualificationResultId, PublicationDestination destination, string idempotencyKey, string reason) => new()
        {
            QualificationResultId = qualificationResultId,
            Destination = destination,
            IdempotencyKey = idempotencyKey,
            Status = PublicationStatus.Skipped,
            SkippedReason = reason,
        };

    public void MarkSent(string? externalId, DateTimeOffset sentAt)
    {
        Status = PublicationStatus.Sent;
        ExternalId = externalId;
        SentAt = sentAt;
        AttemptCount++;
    }

    // FR-027, Acceptance Scenario 3: a transient failure — remains retryable.
    public void MarkFailed(string error)
    {
        Status = PublicationStatus.Failed;
        LastError = error;
        AttemptCount++;
    }

    // Acceptance Scenario 5: a permanent rejection — surfaced, never retried again.
    public void MarkRejected(string reason)
    {
        Status = PublicationStatus.Rejected;
        LastError = reason;
        AttemptCount++;
    }

    public void MarkRetracted(string reason, DateTimeOffset correctedAt)
    {
        Status = PublicationStatus.Retracted;
        Correction = new PublicationCorrection(CorrectionType.Retract, reason, DestinationAccepted: true);
        CorrectedAt = correctedAt;
    }

    public void MarkAdjusted(string reason, DateTimeOffset correctedAt)
    {
        Status = PublicationStatus.Adjusted;
        Correction = new PublicationCorrection(CorrectionType.Adjust, reason, DestinationAccepted: true);
        CorrectedAt = correctedAt;
    }

    // FR-044: GA4's Measurement Protocol offers no retraction — the original event stands;
    // this records the divergence rather than reporting it as corrected. Status is
    // deliberately left as-is (still Sent) since nothing changed at the destination.
    public void MarkUnpropagatable(string reason, DateTimeOffset correctedAt)
    {
        Correction = new PublicationCorrection(CorrectionType.Unpropagatable, reason, DestinationAccepted: false);
        CorrectedAt = correctedAt;
    }

    internal static ConversionPublication Rehydrate(
        Guid id, Guid qualificationResultId, PublicationDestination destination, string idempotencyKey,
        PublicationStatus status, string? skippedReason, int attemptCount, string? externalId, string? lastError,
        PublicationCorrection? correction, DateTimeOffset? sentAt, DateTimeOffset? correctedAt) => new()
        {
            Id = id,
            QualificationResultId = qualificationResultId,
            Destination = destination,
            IdempotencyKey = idempotencyKey,
            Status = status,
            SkippedReason = skippedReason,
            AttemptCount = attemptCount,
            ExternalId = externalId,
            LastError = lastError,
            Correction = correction,
            SentAt = sentAt,
            CorrectedAt = correctedAt,
        };
}
