using System.Data;

namespace Attribution.Infrastructure.Data;

// A single atomic database transaction spanning one or more repository calls — used
// wherever the spec requires two writes to succeed or fail together (e.g. FR-003's
// atomic allocation, or writing a Conversion Publication outbox row in the same
// transaction as the qualification decision that produced it, research.md §3).
public interface IUnitOfWork
{
    Task<TResult> ExecuteAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> work);

    Task ExecuteAsync(Func<IDbConnection, IDbTransaction, Task> work);
}
