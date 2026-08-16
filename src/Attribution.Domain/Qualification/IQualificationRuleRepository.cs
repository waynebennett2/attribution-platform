namespace Attribution.Domain.Qualification;

public interface IQualificationRuleRepository
{
    Task<QualificationRule?> GetByIdAsync(Guid id);

    // FR-024: the single version currently governing this scope at the given instant.
    Task<QualificationRule?> GetInForceAsync(QualificationScopeType scopeType, string? scopeRef, DateTimeOffset instant);

    // RuleVersioningService's contiguity check reads this: the scope's currently-open
    // version (EffectiveEnd is null), or null if the scope has no version at all yet.
    Task<QualificationRule?> GetLatestVersionAsync(QualificationScopeType scopeType, string? scopeRef);

    Task<IReadOnlyList<QualificationRule>> GetByScopeAsync(QualificationScopeType scopeType, string? scopeRef);

    Task AddAsync(QualificationRule rule);

    Task UpdateAsync(QualificationRule rule);

    Task DeleteAsync(Guid id);
}
