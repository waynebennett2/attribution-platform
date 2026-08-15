using Attribution.Domain.Calls;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class CallRepository : RepositoryBase, ICallRepository
{
    public CallRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Call?> GetBySourceRecordIdAsync(string sourceRecordId)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<CallRow>(
            "SELECT * FROM calls WHERE source_record_id = @SourceRecordId", new { SourceRecordId = sourceRecordId });
        return row?.ToDomain();
    }

    public async Task<Call?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<CallRow>(
            "SELECT * FROM calls WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task AddAsync(Call call)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO calls
                (id, source_record_id, direction, dialled_number, caller_id, started_at, answered_at,
                 ended_at, connected_duration_seconds, disposition, is_final, ingested_at, updated_at)
            VALUES
                (@Id, @SourceRecordId, @Direction, @DialledNumber, @CallerId, @StartedAt, @AnsweredAt,
                 @EndedAt, @ConnectedDurationSeconds, @Disposition, @IsFinal, @IngestedAt, @UpdatedAt)
            """,
            CallRow.FromDomain(call));
    }

    public async Task UpdateAsync(Call call)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE calls SET
                answered_at = @AnsweredAt, ended_at = @EndedAt,
                connected_duration_seconds = @ConnectedDurationSeconds, disposition = @Disposition,
                is_final = @IsFinal, updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            CallRow.FromDomain(call));
    }

    private sealed class CallRow
    {
        public string Id { get; set; } = string.Empty;
        public string SourceRecordId { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string DialledNumber { get; set; } = string.Empty;
        public string? CallerId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? AnsweredAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? ConnectedDurationSeconds { get; set; }
        public string? Disposition { get; set; }
        public bool IsFinal { get; set; }
        public DateTimeOffset IngestedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public Call ToDomain() => Call.Rehydrate(
            Guid.Parse(Id), SourceRecordId, Enum.Parse<CallDirection>(Direction), DialledNumber, CallerId,
            StartedAt, AnsweredAt, EndedAt, ConnectedDurationSeconds, Disposition, IsFinal, IngestedAt, UpdatedAt);

        public static object FromDomain(Call call) => new
        {
            Id = call.Id.ToString(),
            call.SourceRecordId,
            Direction = call.Direction.ToString(),
            call.DialledNumber,
            call.CallerId,
            call.StartedAt,
            call.AnsweredAt,
            call.EndedAt,
            call.ConnectedDurationSeconds,
            call.Disposition,
            call.IsFinal,
            call.IngestedAt,
            call.UpdatedAt,
        };
    }
}
