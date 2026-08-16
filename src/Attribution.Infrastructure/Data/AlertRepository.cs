using Attribution.Domain.Audit;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class AlertRepository : RepositoryBase, IAlertRepository
{
    public AlertRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Alert?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<AlertRow>(
            "SELECT * FROM alerts WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<Alert?> GetOpenAsync(AlertConditionType conditionType, string? scopeRef)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<AlertRow>(
            "SELECT * FROM alerts WHERE condition_type = @ConditionType AND scope_ref <=> @ScopeRef AND cleared_at IS NULL",
            new { ConditionType = conditionType.ToString(), ScopeRef = scopeRef });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Alert>> GetOpenAsync()
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<AlertRow>("SELECT * FROM alerts WHERE cleared_at IS NULL ORDER BY raised_at ASC");
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(Alert alert)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO alerts (id, condition_type, scope_ref, threshold, raised_at, last_notified_at, acknowledged_at, acknowledged_by, cleared_at)
            VALUES (@Id, @ConditionType, @ScopeRef, @Threshold, @RaisedAt, @LastNotifiedAt, @AcknowledgedAt, @AcknowledgedBy, @ClearedAt)
            """,
            RowFromDomain(alert));
    }

    public async Task UpdateAsync(Alert alert)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE alerts SET
                last_notified_at = @LastNotifiedAt, acknowledged_at = @AcknowledgedAt,
                acknowledged_by = @AcknowledgedBy, cleared_at = @ClearedAt
            WHERE id = @Id
            """,
            RowFromDomain(alert));
    }

    private static object RowFromDomain(Alert alert) => new
    {
        Id = alert.Id.ToString(),
        ConditionType = alert.ConditionType.ToString(),
        alert.ScopeRef,
        alert.Threshold,
        alert.RaisedAt,
        alert.LastNotifiedAt,
        alert.AcknowledgedAt,
        alert.AcknowledgedBy,
        alert.ClearedAt,
    };

    private sealed class AlertRow
    {
        public string Id { get; set; } = string.Empty;
        public string ConditionType { get; set; } = string.Empty;
        public string? ScopeRef { get; set; }
        public string Threshold { get; set; } = string.Empty;
        public DateTimeOffset RaisedAt { get; set; }
        public DateTimeOffset LastNotifiedAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public string? AcknowledgedBy { get; set; }
        public DateTimeOffset? ClearedAt { get; set; }

        public Alert ToDomain() => Alert.Rehydrate(
            Guid.Parse(Id), Enum.Parse<AlertConditionType>(ConditionType), ScopeRef, Threshold, RaisedAt,
            LastNotifiedAt, AcknowledgedAt, AcknowledgedBy, ClearedAt);
    }
}
