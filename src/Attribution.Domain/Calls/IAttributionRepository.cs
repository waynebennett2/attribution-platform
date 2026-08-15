namespace Attribution.Domain.Calls;

public interface IAttributionRepository
{
    // The live decision for a call — re-derivation (FR-045) reads this to know what to
    // supersede, and reporting reads it to know what a call's status currently is.
    Task<Attribution?> GetCurrentByCallIdAsync(Guid callId);

    Task AddAsync(Attribution attribution);

    // Persists a Supersede() call — IsCurrent/SupersededReason only, the row otherwise
    // stays exactly as originally decided.
    Task UpdateAsync(Attribution attribution);
}
