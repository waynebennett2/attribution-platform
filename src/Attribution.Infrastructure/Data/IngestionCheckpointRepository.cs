using Attribution.Domain.Calls;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class IngestionCheckpointRepository : RepositoryBase, IIngestionCheckpointRepository
{
    public IngestionCheckpointRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<IngestionCheckpoint?> GetByFeedAsync(string feed)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<IngestionCheckpointRow>(
            "SELECT * FROM ingestion_checkpoints WHERE feed = @Feed", new { Feed = feed });
        return row?.ToDomain();
    }

    public async Task AddAsync(IngestionCheckpoint checkpoint)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "INSERT INTO ingestion_checkpoints (id, feed, position, updated_at) VALUES (@Id, @Feed, @Position, @UpdatedAt)",
            IngestionCheckpointRow.FromDomain(checkpoint));
    }

    public async Task UpdateAsync(IngestionCheckpoint checkpoint)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE ingestion_checkpoints SET position = @Position, updated_at = @UpdatedAt WHERE id = @Id",
            IngestionCheckpointRow.FromDomain(checkpoint));
    }

    private sealed class IngestionCheckpointRow
    {
        public string Id { get; set; } = string.Empty;
        public string Feed { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }

        public IngestionCheckpoint ToDomain() => IngestionCheckpoint.Rehydrate(Guid.Parse(Id), Feed, Position, UpdatedAt);

        public static object FromDomain(IngestionCheckpoint checkpoint) => new
        {
            Id = checkpoint.Id.ToString(),
            checkpoint.Feed,
            checkpoint.Position,
            checkpoint.UpdatedAt,
        };
    }
}
