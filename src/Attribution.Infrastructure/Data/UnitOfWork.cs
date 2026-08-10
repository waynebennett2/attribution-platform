using System.Data;

namespace Attribution.Infrastructure.Data;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UnitOfWork(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> work)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var result = await work(connection, transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public Task ExecuteAsync(Func<IDbConnection, IDbTransaction, Task> work) =>
        ExecuteAsync(async (connection, transaction) =>
        {
            await work(connection, transaction);
            return true;
        });
}
