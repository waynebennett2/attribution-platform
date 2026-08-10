using System.Data;

namespace Attribution.Infrastructure.Data;

// Infrastructure-layer abstraction over connection creation, so repositories don't take
// a direct dependency on the connection string / provider (Dapper needs an open
// IDbConnection per call; this is the one place that knows how to create one).
public interface IDbConnectionFactory
{
    IDbConnection CreateOpenConnection();
}
