using System.Data;
using Dapper;
using MySqlConnector;

namespace Attribution.Infrastructure.Data;

public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    // Runs once per process, the first time any repository is used — the single choke
    // point every repository passes through, so registering it here (rather than in each
    // host's Program.cs, which is easy to forget per-host, as Attribution.Workers already
    // once did for DefaultTypeMap.MatchNamesWithUnderscores) guarantees it's always in
    // place. See DateTimeOffsetTypeHandler for why it's needed.
    static MySqlConnectionFactory()
    {
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }

    public MySqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateOpenConnection()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
