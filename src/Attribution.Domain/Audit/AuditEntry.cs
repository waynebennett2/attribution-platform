namespace Attribution.Domain.Audit;

// FR-035: an immutable record of one administrator action. Every setter is private and
// there is no method that mutates state after Create() — immutability is enforced by
// the type itself, not merely by a database grant (see AuditImmutabilityTests).
public class AuditEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string ActorUserId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string TargetId { get; private set; } = string.Empty;
    public string? BeforeValue { get; private set; }
    public string AfterValue { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; } = DateTimeOffset.UtcNow;

    private AuditEntry() { }

    public static AuditEntry Create(
        string actorUserId,
        string action,
        string targetType,
        string targetId,
        string? beforeValue,
        string afterValue)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("An audit entry must record an actor.", nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("An audit entry must record an action.", nameof(action));
        }

        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("An audit entry must record what it targeted.");
        }

        return new AuditEntry
        {
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            BeforeValue = beforeValue,
            AfterValue = afterValue ?? string.Empty,
        };
    }

    // Infrastructure-only reconstruction from stored state (see AssemblyInfo.cs). Note this
    // is still not a mutation path — it produces a new instance, never edits a stored one.
    internal static AuditEntry Rehydrate(
        Guid id,
        string actorUserId,
        string action,
        string targetType,
        string targetId,
        string? beforeValue,
        string afterValue,
        DateTimeOffset occurredAt) => new()
        {
            Id = id,
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            BeforeValue = beforeValue,
            AfterValue = afterValue,
            OccurredAt = occurredAt,
        };
}
