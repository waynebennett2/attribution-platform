namespace Attribution.Domain.Audit;

public interface IAlertRepository
{
    Task<Alert?> GetByIdAsync(Guid id);

    // FR-047's invariant: at most one open row per (conditionType, scopeRef).
    Task<Alert?> GetOpenAsync(AlertConditionType conditionType, string? scopeRef);

    Task<IReadOnlyList<Alert>> GetOpenAsync();

    Task AddAsync(Alert alert);

    Task UpdateAsync(Alert alert);
}
