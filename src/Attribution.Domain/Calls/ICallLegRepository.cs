namespace Attribution.Domain.Calls;

public interface ICallLegRepository
{
    // Idempotent upsert lookup — matches the unique (source_call_record_id, source_leg_id) pair.
    Task<CallLeg?> GetBySourceIdsAsync(string sourceCallRecordId, string sourceLegId);

    // Legs that arrived before their parent Call Detail Record (spec.md "Ingestion
    // resilience") — awaiting LinkToCall once that CDR is ingested.
    Task<IReadOnlyList<CallLeg>> GetOrphanedBySourceCallRecordIdAsync(string sourceCallRecordId);

    Task AddAsync(CallLeg leg);

    Task UpdateAsync(CallLeg leg);
}
