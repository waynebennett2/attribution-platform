namespace Attribution.Domain.Audit;

// FR-035: append-only by contract — deliberately, there is no Update or Delete method
// on this interface. The database-level enforcement (no UPDATE/DELETE grant on the
// audit_entries table) is a deployment concern layered on top of this.
public interface IAuditRepository
{
    Task AddAsync(AuditEntry entry);

    Task<IReadOnlyList<AuditEntry>> GetByTargetAsync(string targetType, string targetId);

    // contracts/admin-api.md's read-only query endpoint: every filter is independently
    // optional, so an omitted one imposes no constraint rather than matching nothing.
    Task<IReadOnlyList<AuditEntry>> QueryAsync(string? targetType, string? targetId, DateTimeOffset? from, DateTimeOffset? to);
}
