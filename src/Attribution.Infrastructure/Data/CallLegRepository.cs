using Attribution.Domain.Calls;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class CallLegRepository : RepositoryBase, ICallLegRepository
{
    public CallLegRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<CallLeg?> GetBySourceIdsAsync(string sourceCallRecordId, string sourceLegId)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<CallLegRow>(
            """
            SELECT * FROM call_legs
            WHERE source_call_record_id = @SourceCallRecordId AND source_leg_id = @SourceLegId
            """,
            new { SourceCallRecordId = sourceCallRecordId, SourceLegId = sourceLegId });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<CallLeg>> GetOrphanedBySourceCallRecordIdAsync(string sourceCallRecordId)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<CallLegRow>(
            "SELECT * FROM call_legs WHERE source_call_record_id = @SourceCallRecordId AND call_id IS NULL",
            new { SourceCallRecordId = sourceCallRecordId });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(CallLeg leg)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO call_legs
                (id, call_id, source_call_record_id, source_leg_id, sequence_or_role, started_at, ended_at)
            VALUES
                (@Id, @CallId, @SourceCallRecordId, @SourceLegId, @SequenceOrRole, @StartedAt, @EndedAt)
            """,
            CallLegRow.FromDomain(leg));
    }

    public async Task UpdateAsync(CallLeg leg)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE call_legs SET call_id = @CallId WHERE id = @Id",
            new { Id = leg.Id.ToString(), CallId = leg.CallId?.ToString() });
    }

    private sealed class CallLegRow
    {
        public string Id { get; set; } = string.Empty;
        public string? CallId { get; set; }
        public string SourceCallRecordId { get; set; } = string.Empty;
        public string SourceLegId { get; set; } = string.Empty;
        public string? SequenceOrRole { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }

        public CallLeg ToDomain() => CallLeg.Rehydrate(
            Guid.Parse(Id), CallId is null ? null : Guid.Parse(CallId), SourceCallRecordId, SourceLegId,
            SequenceOrRole, StartedAt, EndedAt);

        public static object FromDomain(CallLeg leg) => new
        {
            Id = leg.Id.ToString(),
            CallId = leg.CallId?.ToString(),
            leg.SourceCallRecordId,
            leg.SourceLegId,
            leg.SequenceOrRole,
            leg.StartedAt,
            leg.EndedAt,
        };
    }
}
