using Attribution.Domain.Pools;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class TrackingNumberRepository : RepositoryBase, ITrackingNumberRepository
{
    public TrackingNumberRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<TrackingNumber?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<TrackingNumberRow>(
            "SELECT * FROM tracking_numbers WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<TrackingNumber?> GetByDidAsync(string did)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<TrackingNumberRow>(
            "SELECT * FROM tracking_numbers WHERE did = @Did", new { Did = did });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<TrackingNumber>> GetByPoolAsync(Guid poolId)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<TrackingNumberRow>(
            "SELECT * FROM tracking_numbers WHERE pool_id = @PoolId", new { PoolId = poolId.ToString() });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(TrackingNumber number)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at, last_released_at)
            VALUES (@Id, @PoolId, @Did, @Status, @StatusChangedAt, @LastReleasedAt)
            """,
            TrackingNumberRow.FromDomain(number));
    }

    public async Task UpdateAsync(TrackingNumber number)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE tracking_numbers SET
                pool_id = @PoolId, status = @Status, status_changed_at = @StatusChangedAt,
                last_released_at = @LastReleasedAt
            WHERE id = @Id
            """,
            TrackingNumberRow.FromDomain(number));
    }

    private sealed class TrackingNumberRow
    {
        public string Id { get; set; } = string.Empty;
        public string PoolId { get; set; } = string.Empty;
        public string Did { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset StatusChangedAt { get; set; }
        public DateTimeOffset? LastReleasedAt { get; set; }

        public TrackingNumber ToDomain() => TrackingNumber.Rehydrate(
            Guid.Parse(Id), Guid.Parse(PoolId), Did, Enum.Parse<TrackingNumberStatus>(Status),
            StatusChangedAt, LastReleasedAt);

        public static object FromDomain(TrackingNumber number) => new
        {
            Id = number.Id.ToString(),
            PoolId = number.PoolId.ToString(),
            number.Did,
            Status = number.Status.ToString(),
            number.StatusChangedAt,
            number.LastReleasedAt,
        };
    }
}
