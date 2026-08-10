using System.Data;

namespace Attribution.Infrastructure.Data;

// research.md §3: the outbox pattern used by publication. A row is written in the same
// transaction as the business decision that produced it (e.g. a qualification result),
// so a crash between "decided" and "recorded" can never happen; a worker later drains
// rows in `pending` status. This is generic plumbing — the entity-specific write
// (Conversion Publication) is added once that entity exists (T078/T081).
public interface IOutboxWriter
{
    Task WriteAsync(IDbConnection connection, IDbTransaction transaction, string tableName, object row);
}
