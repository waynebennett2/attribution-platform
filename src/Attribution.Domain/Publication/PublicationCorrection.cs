namespace Attribution.Domain.Publication;

public enum CorrectionType
{
    Retract,
    Adjust,
    Unpropagatable,
}

// FR-044: what happened when a call's already-published qualification changed.
// DestinationAccepted is false only for Unpropagatable (GA4) — the destination could not
// act on the correction at all, so the original event stands, knowingly divergent.
public sealed record PublicationCorrection(CorrectionType Type, string Reason, bool DestinationAccepted);
