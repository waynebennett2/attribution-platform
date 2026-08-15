namespace Attribution.Domain.Calls;

public interface ICallRepository
{
    // FR-017: idempotent upsert lookup — 8x8's own record identifier is the natural key.
    Task<Call?> GetBySourceRecordIdAsync(string sourceRecordId);

    Task<Call?> GetByIdAsync(Guid id);

    Task AddAsync(Call call);

    Task UpdateAsync(Call call);
}
