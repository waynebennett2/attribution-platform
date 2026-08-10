using Attribution.Domain.Pools;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class NumberPoolRepository : RepositoryBase, INumberPoolRepository
{
    public NumberPoolRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<NumberPool?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PoolRow>(
            "SELECT * FROM number_pools WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<NumberPool>> GetByScopeAsync(string scopeType, Guid scopeRef)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<PoolRow>(
            "SELECT * FROM number_pools WHERE scope_type = @ScopeType AND scope_ref = @ScopeRef",
            new { ScopeType = scopeType, ScopeRef = scopeRef.ToString() });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(NumberPool pool)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO number_pools (id, name, scope_type, scope_ref, default_number, created_at, updated_at)
            VALUES (@Id, @Name, @ScopeType, @ScopeRef, @DefaultNumber, @CreatedAt, @UpdatedAt)
            """,
            new
            {
                Id = pool.Id.ToString(),
                pool.Name,
                pool.ScopeType,
                ScopeRef = pool.ScopeRef.ToString(),
                pool.DefaultNumber,
                pool.CreatedAt,
                pool.UpdatedAt,
            });
    }

    private sealed class PoolRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ScopeType { get; set; } = string.Empty;
        public string ScopeRef { get; set; } = string.Empty;
        public string? DefaultNumber { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public NumberPool ToDomain() => NumberPool.Rehydrate(
            Guid.Parse(Id), Name, ScopeType, Guid.Parse(ScopeRef), DefaultNumber, CreatedAt, UpdatedAt);
    }
}
