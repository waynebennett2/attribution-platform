namespace Attribution.Domain.Calls;

public interface IIngestionCheckpointRepository
{
    Task<IngestionCheckpoint?> GetByFeedAsync(string feed);

    Task AddAsync(IngestionCheckpoint checkpoint);

    Task UpdateAsync(IngestionCheckpoint checkpoint);
}
