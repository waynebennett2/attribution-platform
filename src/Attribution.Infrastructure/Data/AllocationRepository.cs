using System.Data;
using Attribution.Domain.Sessions;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class AllocationRepository : RepositoryBase, IAllocationRepository
{
    public AllocationRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Allocation?> GetBySessionIdAsync(Guid sessionId)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<AllocationRow>(
            "SELECT * FROM allocations WHERE session_id = @SessionId", new { SessionId = sessionId.ToString() });
        return row?.ToDomain();
    }

    // FR-018: attribution's core lookup — which allocation(s) held this number at the
    // call's start time. More than one row back means ambiguous (FR-021); zero means
    // unattributed (FR-020).
    public async Task<IReadOnlyList<Allocation>> GetCoveringInstantAsync(Guid trackingNumberId, DateTimeOffset instant)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<AllocationRow>(
            """
            SELECT * FROM allocations
            WHERE tracking_number_id = @TrackingNumberId AND window_start <= @Instant AND window_end > @Instant
            """,
            new { TrackingNumberId = trackingNumberId.ToString(), Instant = instant });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(Allocation allocation)
    {
        using var connection = OpenConnection();
        await InsertAsync(connection, transaction: null, allocation);
    }

    public async Task UpdateAsync(Allocation allocation)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE allocations SET window_end = @WindowEnd WHERE id = @Id",
            new { Id = allocation.Id.ToString(), allocation.WindowEnd });
    }

    public static Task InsertAsync(IDbConnection connection, IDbTransaction? transaction, Allocation allocation) =>
        connection.ExecuteAsync(
            """
            INSERT INTO allocations
                (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
            VALUES
                (@Id, @TrackingNumberId, @SessionId, @PoolIdAtAllocation, @WindowStart, @WindowEnd, @IsShadow, @CreatedAt)
            """,
            new
            {
                Id = allocation.Id.ToString(),
                TrackingNumberId = allocation.TrackingNumberId.ToString(),
                SessionId = allocation.SessionId.ToString(),
                PoolIdAtAllocation = allocation.PoolIdAtAllocation?.ToString(),
                allocation.WindowStart,
                allocation.WindowEnd,
                allocation.IsShadow,
                allocation.CreatedAt,
            },
            transaction);

    private sealed class AllocationRow
    {
        public string Id { get; set; } = string.Empty;
        public string TrackingNumberId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string? PoolIdAtAllocation { get; set; }
        public DateTimeOffset WindowStart { get; set; }
        public DateTimeOffset WindowEnd { get; set; }
        public bool IsShadow { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public Allocation ToDomain() => Allocation.Rehydrate(
            Guid.Parse(Id), Guid.Parse(TrackingNumberId), Guid.Parse(SessionId),
            PoolIdAtAllocation is null ? null : Guid.Parse(PoolIdAtAllocation),
            WindowStart, WindowEnd, IsShadow, CreatedAt);
    }
}
