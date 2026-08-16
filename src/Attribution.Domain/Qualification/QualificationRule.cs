namespace Attribution.Domain.Qualification;

// FR-023, FR-024: a versioned, scoped condition set. Within one (ScopeType, ScopeRef),
// consecutive versions' effective periods are contiguous and non-overlapping by
// construction — RuleVersioningService derives a new version's predecessor's
// EffectiveEnd from the new version's EffectiveStart, so a gap or overlap can only arise
// from an invalid request, which that service rejects before a row like this ever exists.
public class QualificationRule
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public QualificationScopeType ScopeType { get; private set; }
    public string? ScopeRef { get; private set; }
    public int Version { get; private set; }
    public QualificationConditions Conditions { get; private set; } = QualificationConditions.Default;
    public DateTimeOffset EffectiveStart { get; private set; }
    public DateTimeOffset? EffectiveEnd { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private QualificationRule() { }

    public static QualificationRule Create(
        QualificationScopeType scopeType,
        string? scopeRef,
        int version,
        QualificationConditions conditions,
        DateTimeOffset effectiveStart,
        DateTimeOffset? effectiveEnd,
        string createdBy,
        DateTimeOffset createdAt)
    {
        if (scopeType != QualificationScopeType.Default && string.IsNullOrWhiteSpace(scopeRef))
        {
            throw new ArgumentException("A website- or campaign-scoped rule must specify a scope_ref.", nameof(scopeRef));
        }

        return new QualificationRule
        {
            ScopeType = scopeType,
            ScopeRef = scopeType == QualificationScopeType.Default ? null : scopeRef,
            Version = version,
            Conditions = conditions,
            EffectiveStart = effectiveStart,
            EffectiveEnd = effectiveEnd,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
        };
    }

    // FR-024: exactly one version of a scope's rule governs any given instant — this is
    // that check.
    public bool IsInForceAt(DateTimeOffset instant) =>
        instant >= EffectiveStart && (EffectiveEnd is null || instant < EffectiveEnd);

    // Closes this version's open end — called only when a successor version is created
    // starting at exactly this instant (RuleVersioningService), or reopened (set back to
    // null) if that successor is later deleted while still a not-yet-effective future
    // version (FR-024's delete-future-only rule).
    public void SetEffectiveEnd(DateTimeOffset? effectiveEnd) => EffectiveEnd = effectiveEnd;

    internal static QualificationRule Rehydrate(
        Guid id,
        QualificationScopeType scopeType,
        string? scopeRef,
        int version,
        QualificationConditions conditions,
        DateTimeOffset effectiveStart,
        DateTimeOffset? effectiveEnd,
        string createdBy,
        DateTimeOffset createdAt) => new()
        {
            Id = id,
            ScopeType = scopeType,
            ScopeRef = scopeRef,
            Version = version,
            Conditions = conditions,
            EffectiveStart = effectiveStart,
            EffectiveEnd = effectiveEnd,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
        };
}
