using Attribution.Domain.Calls;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class AttributionRepository : RepositoryBase, IAttributionRepository
{
    public AttributionRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Domain.Calls.Attribution?> GetCurrentByCallIdAsync(Guid callId)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<AttributionRow>(
            "SELECT * FROM attributions WHERE call_id = @CallId AND is_current = 1",
            new { CallId = callId.ToString() });
        return row?.ToDomain();
    }

    public async Task AddAsync(Domain.Calls.Attribution attribution)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO attributions
                (id, call_id, session_id, allocation_id, state, reason, is_shadow_derived, is_current,
                 superseded_reason, decided_at)
            VALUES
                (@Id, @CallId, @SessionId, @AllocationId, @State, @Reason, @IsShadowDerived, @IsCurrent,
                 @SupersededReason, @DecidedAt)
            """,
            AttributionRow.FromDomain(attribution));
    }

    public async Task UpdateAsync(Domain.Calls.Attribution attribution)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE attributions SET is_current = @IsCurrent, superseded_reason = @SupersededReason WHERE id = @Id",
            new
            {
                Id = attribution.Id.ToString(),
                attribution.IsCurrent,
                attribution.SupersededReason,
            });
    }

    private sealed class AttributionRow
    {
        public string Id { get; set; } = string.Empty;
        public string CallId { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? AllocationId { get; set; }
        public string State { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public bool IsShadowDerived { get; set; }
        public bool IsCurrent { get; set; }
        public string? SupersededReason { get; set; }
        public DateTimeOffset DecidedAt { get; set; }

        public Domain.Calls.Attribution ToDomain() => Domain.Calls.Attribution.Rehydrate(
            Guid.Parse(Id), Guid.Parse(CallId), SessionId is null ? null : Guid.Parse(SessionId),
            AllocationId is null ? null : Guid.Parse(AllocationId), Enum.Parse<AttributionState>(State),
            Reason, IsShadowDerived, IsCurrent, SupersededReason, DecidedAt);

        public static object FromDomain(Domain.Calls.Attribution attribution) => new
        {
            Id = attribution.Id.ToString(),
            CallId = attribution.CallId.ToString(),
            SessionId = attribution.SessionId?.ToString(),
            AllocationId = attribution.AllocationId?.ToString(),
            State = attribution.State.ToString(),
            attribution.Reason,
            attribution.IsShadowDerived,
            attribution.IsCurrent,
            attribution.SupersededReason,
            attribution.DecidedAt,
        };
    }
}
