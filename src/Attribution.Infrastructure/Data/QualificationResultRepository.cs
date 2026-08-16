using Attribution.Domain.Qualification;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class QualificationResultRepository : RepositoryBase, IQualificationResultRepository
{
    public QualificationResultRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<QualificationResult?> GetCurrentByCallIdAsync(Guid callId)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<QualificationResultRow>(
            "SELECT * FROM qualification_results WHERE call_id = @CallId AND is_current = 1",
            new { CallId = callId.ToString() });
        return row?.ToDomain();
    }

    public async Task AddAsync(QualificationResult result)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO qualification_results
                (id, call_id, attribution_id, qualification_rule_id, is_qualified, is_current, superseded_reason, decided_at)
            VALUES
                (@Id, @CallId, @AttributionId, @QualificationRuleId, @IsQualified, @IsCurrent, @SupersededReason, @DecidedAt)
            """,
            QualificationResultRow.FromDomain(result));
    }

    public async Task UpdateAsync(QualificationResult result)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE qualification_results SET is_current = @IsCurrent, superseded_reason = @SupersededReason WHERE id = @Id",
            new
            {
                Id = result.Id.ToString(),
                result.IsCurrent,
                result.SupersededReason,
            });
    }

    private sealed class QualificationResultRow
    {
        public string Id { get; set; } = string.Empty;
        public string CallId { get; set; } = string.Empty;
        public string AttributionId { get; set; } = string.Empty;
        public string QualificationRuleId { get; set; } = string.Empty;
        public bool IsQualified { get; set; }
        public bool IsCurrent { get; set; }
        public string? SupersededReason { get; set; }
        public DateTimeOffset DecidedAt { get; set; }

        public QualificationResult ToDomain() => QualificationResult.Rehydrate(
            Guid.Parse(Id), Guid.Parse(CallId), Guid.Parse(AttributionId), Guid.Parse(QualificationRuleId),
            IsQualified, IsCurrent, SupersededReason, DecidedAt);

        public static object FromDomain(QualificationResult result) => new
        {
            Id = result.Id.ToString(),
            CallId = result.CallId.ToString(),
            AttributionId = result.AttributionId.ToString(),
            QualificationRuleId = result.QualificationRuleId.ToString(),
            result.IsQualified,
            result.IsCurrent,
            result.SupersededReason,
            result.DecidedAt,
        };
    }
}
