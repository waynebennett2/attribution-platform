namespace Attribution.Domain.Publication;

// A retryable outbox row plus the call/session data needed to actually build the request
// to the destination — PublicationWorker has no reason to separately fetch the Call and
// Session for every row it drains, so the repository resolves the join once.
public sealed record PublicationWorkItem(
    ConversionPublication Publication,
    Guid CallId,
    DateTimeOffset CallStartedAt,
    string? Gclid,
    string? Gbraid,
    string? Wbraid,
    string? Ga4ClientId);
