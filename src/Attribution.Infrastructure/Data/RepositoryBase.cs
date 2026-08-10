using System.Data;

namespace Attribution.Infrastructure.Data;

// Shared base for Dapper repositories: every concrete repository gets a connection
// factory and a short-hand for opening a connection for a single Dapper call. Multi-step
// atomic operations use IUnitOfWork instead of this class's OpenConnection().
public abstract class RepositoryBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    protected RepositoryBase(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    protected IDbConnection OpenConnection() => _connectionFactory.CreateOpenConnection();
}
