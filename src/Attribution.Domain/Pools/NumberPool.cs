namespace Attribution.Domain.Pools;

// FR-001, FR-004: a named collection of tracking numbers, scoped to a website, campaign
// or business unit.
public class NumberPool
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string ScopeType { get; private set; } = string.Empty; // "website" | "campaign" | "business_unit"
    public Guid ScopeRef { get; private set; }
    public string? DefaultNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private NumberPool() { }

    public static NumberPool Create(string name, string scopeType, Guid scopeRef, string? defaultNumber = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A pool must have a name.", nameof(name));
        }

        if (scopeType is not ("website" or "campaign" or "business_unit"))
        {
            throw new ArgumentException("Scope type must be website, campaign or business_unit (FR-004).", nameof(scopeType));
        }

        return new NumberPool { Name = name, ScopeType = scopeType, ScopeRef = scopeRef, DefaultNumber = defaultNumber };
    }

    internal static NumberPool Rehydrate(
        Guid id, string name, string scopeType, Guid scopeRef, string? defaultNumber,
        DateTimeOffset createdAt, DateTimeOffset updatedAt) => new()
        {
            Id = id,
            Name = name,
            ScopeType = scopeType,
            ScopeRef = scopeRef,
            DefaultNumber = defaultNumber,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
}
