namespace Attribution.Domain.Pools;

public interface INumberPoolRepository
{
    Task<NumberPool?> GetByIdAsync(Guid id);

    Task<IReadOnlyList<NumberPool>> GetByScopeAsync(string scopeType, Guid scopeRef);

    Task AddAsync(NumberPool pool);
}
