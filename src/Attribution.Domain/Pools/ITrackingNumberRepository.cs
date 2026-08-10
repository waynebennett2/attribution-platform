namespace Attribution.Domain.Pools;

public interface ITrackingNumberRepository
{
    Task<TrackingNumber?> GetByIdAsync(Guid id);

    Task<TrackingNumber?> GetByDidAsync(string did);

    Task<IReadOnlyList<TrackingNumber>> GetByPoolAsync(Guid poolId);

    Task AddAsync(TrackingNumber number);

    Task UpdateAsync(TrackingNumber number);
}
