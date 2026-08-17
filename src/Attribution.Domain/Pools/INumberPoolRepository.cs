namespace Attribution.Domain.Pools;

public interface INumberPoolRepository
{
    Task<NumberPool?> GetByIdAsync(Guid id);

    Task<IReadOnlyList<NumberPool>> GetByScopeAsync(string scopeType, Guid scopeRef);

    Task<IReadOnlyList<NumberPool>> GetAllAsync();

    Task AddAsync(NumberPool pool);
}
