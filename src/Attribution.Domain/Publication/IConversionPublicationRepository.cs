namespace Attribution.Domain.Publication;

public interface IConversionPublicationRepository
{
    // The current, non-retracted publication for this call+destination, if any — at most
    // one exists at a time: a new row is only ever created once the prior one (if any) was
    // retracted, which is what makes this the right place to look both for
    // PublicationService's idempotent-enqueue check and CorrectionService's "what needs
    // correcting" lookup.
    Task<ConversionPublication?> GetActiveForCallAsync(Guid callId, PublicationDestination destination);

    // FR-027, FR-028: rows the PublicationWorker should attempt — Pending (never tried) or
    // Failed with fewer than maxAttempts tries so far (transient-failure retry) — each
    // joined with the call/session data needed to build the destination request.
    Task<IReadOnlyList<PublicationWorkItem>> GetRetryableAsync(int maxAttempts, int limit);

    Task AddAsync(ConversionPublication publication);

    Task UpdateAsync(ConversionPublication publication);
}
