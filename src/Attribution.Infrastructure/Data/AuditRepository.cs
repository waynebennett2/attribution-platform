using System.Data;
using Attribution.Domain.Audit;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class AuditRepository : RepositoryBase, IAuditRepository
{
    public AuditRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task AddAsync(AuditEntry entry)
    {
        using var connection = OpenConnection();
        await InsertAsync(connection, transaction: null, entry);
    }

    // Used by callers (e.g. audit-logging middleware, T018) that must write the audit
    // entry in the same transaction as the action it records, so the two can never
    // diverge (an action is never committed without its audit entry, or vice versa).
    public static Task InsertAsync(IDbConnection connection, IDbTransaction? transaction, AuditEntry entry) =>
        connection.ExecuteAsync(
            """
            INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
            VALUES (@Id, @ActorUserId, @Action, @TargetType, @TargetId, @BeforeValue, @AfterValue, @OccurredAt)
            """,
            new
            {
                Id = entry.Id.ToString(),
                entry.ActorUserId,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.BeforeValue,
                entry.AfterValue,
                entry.OccurredAt,
            },
            transaction);

    public async Task<IReadOnlyList<AuditEntry>> GetByTargetAsync(string targetType, string targetId)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<AuditEntryRow>(
            "SELECT * FROM audit_entries WHERE target_type = @TargetType AND target_id = @TargetId ORDER BY occurred_at",
            new { TargetType = targetType, TargetId = targetId });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class AuditEntryRow
    {
        public string Id { get; set; } = string.Empty;
        public string ActorUserId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string? BeforeValue { get; set; }
        public string AfterValue { get; set; } = string.Empty;
        public DateTimeOffset OccurredAt { get; set; }

        public AuditEntry ToDomain() => AuditEntry.Rehydrate(
            Guid.Parse(Id), ActorUserId, Action, TargetType, TargetId, BeforeValue, AfterValue, OccurredAt);
    }
}
