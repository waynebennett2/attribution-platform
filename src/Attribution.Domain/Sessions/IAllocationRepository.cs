namespace Attribution.Domain.Sessions;

public interface IAllocationRepository
{
    Task<Allocation?> GetBySessionIdAsync(Guid sessionId);

    // FR-050: a multi-pool session can hold more than one concurrently active Allocation
    // (one per matched pool) — used both to extend every one of them together on a single
    // heartbeat call, and to determine which pool ids an existing session already holds
    // before growing it with newly-matched ones (research.md §15).
    Task<IReadOnlyList<Allocation>> GetAllBySessionIdAsync(Guid sessionId);

    // FR-018: the current holder of a number at a given instant — attribution's core lookup.
    Task<IReadOnlyList<Allocation>> GetCoveringInstantAsync(Guid trackingNumberId, DateTimeOffset instant);

    Task AddAsync(Allocation allocation);

    Task UpdateAsync(Allocation allocation);
}
