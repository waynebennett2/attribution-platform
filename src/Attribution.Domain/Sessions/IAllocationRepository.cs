namespace Attribution.Domain.Sessions;

public interface IAllocationRepository
{
    Task<Allocation?> GetBySessionIdAsync(Guid sessionId);

    // FR-018: the current holder of a number at a given instant — attribution's core lookup.
    Task<IReadOnlyList<Allocation>> GetCoveringInstantAsync(Guid trackingNumberId, DateTimeOffset instant);

    Task AddAsync(Allocation allocation);

    Task UpdateAsync(Allocation allocation);
}
