namespace Attribution.Application.Administration;

// FR-040, FR-039: the raw reads/writes RetentionService's sweep and on-demand erasure both
// drive. Deliberately split from the narrow per-entity repositories (IWebsiteRepository
// etc.) the same way IAlertingMetricsRepository is — these are cross-cutting operations no
// single entity repository's interface expresses, several of them spanning multiple tables
// in one statement by design (e.g. de-identifying a visitor's sessions in the same call).
public interface IRetentionRepository
{
    // --- 14-month step: visitor/session identifiers (research.md §10: hard-deleted, not
    // surrogated — Visitor/Session carry no evidence-chain requirement the way Call does). ---
    Task<IReadOnlyList<Guid>> GetVisitorIdsEligibleForDeIdentificationAsync(DateTimeOffset cutoff);

    Task DeIdentifyVisitorAsync(Guid visitorId, DateTimeOffset now);

    // --- 14-month step: call/publication identifiers (surrogate substitution, in place). ---
    Task<IReadOnlyList<(Guid Id, string? CallerId)>> GetCallsEligibleForDeIdentificationAsync(DateTimeOffset cutoff);

    // FR-040's "except where it is still referenced by an open manual review case".
    Task<bool> HasOpenReviewCaseAsync(Guid callId);

    // A null surrogateCallerId leaves caller_id untouched (nothing to protect — the call
    // never had one) but still marks the row processed, so it isn't reselected every sweep.
    Task DeIdentifyCallAsync(Guid callId, string? surrogateCallerId, DateTimeOffset now);

    Task<IReadOnlyList<(Guid Id, string ExternalId)>> GetPublicationsEligibleForDeIdentificationAsync(DateTimeOffset cutoff);

    Task DeIdentifyPublicationAsync(Guid publicationId, string surrogateExternalId, DateTimeOffset now);

    // --- 25-month step: full purge of a call and everything that hangs off it. ---
    Task<IReadOnlyList<Guid>> GetCallsEligibleForPurgeAsync(DateTimeOffset cutoff);

    Task PurgeCallAsync(Guid callId);

    // --- 7-year audit log retention. ---
    Task PurgeAuditLogOlderThanAsync(DateTimeOffset cutoff);

    // --- FR-039 erasure: everything tied to one visitor, regardless of age. ---
    Task<IReadOnlyList<(Guid Id, string? CallerId)>> GetCallsForVisitorAsync(Guid visitorId);
}
