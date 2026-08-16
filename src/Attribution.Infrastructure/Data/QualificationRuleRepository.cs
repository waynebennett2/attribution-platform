using System.Text.Json;
using Attribution.Domain.Qualification;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class QualificationRuleRepository : RepositoryBase, IQualificationRuleRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public QualificationRuleRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<QualificationRule?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<QualificationRuleRow>(
            "SELECT * FROM qualification_rules WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<QualificationRule?> GetInForceAsync(QualificationScopeType scopeType, string? scopeRef, DateTimeOffset instant)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<QualificationRuleRow>(
            """
            SELECT * FROM qualification_rules
            WHERE scope_type = @ScopeType AND scope_ref <=> @ScopeRef
                AND effective_start <= @Instant AND (effective_end IS NULL OR effective_end > @Instant)
            """,
            new { ScopeType = scopeType.ToString(), ScopeRef = scopeRef, Instant = instant });
        return row?.ToDomain();
    }

    public async Task<QualificationRule?> GetLatestVersionAsync(QualificationScopeType scopeType, string? scopeRef)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<QualificationRuleRow>(
            """
            SELECT * FROM qualification_rules
            WHERE scope_type = @ScopeType AND scope_ref <=> @ScopeRef AND effective_end IS NULL
            """,
            new { ScopeType = scopeType.ToString(), ScopeRef = scopeRef });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<QualificationRule>> GetByScopeAsync(QualificationScopeType scopeType, string? scopeRef)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<QualificationRuleRow>(
            "SELECT * FROM qualification_rules WHERE scope_type = @ScopeType AND scope_ref <=> @ScopeRef",
            new { ScopeType = scopeType.ToString(), ScopeRef = scopeRef });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(QualificationRule rule)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO qualification_rules
                (id, scope_type, scope_ref, version, conditions, effective_start, effective_end, created_by, created_at)
            VALUES
                (@Id, @ScopeType, @ScopeRef, @Version, @Conditions, @EffectiveStart, @EffectiveEnd, @CreatedBy, @CreatedAt)
            """,
            QualificationRuleRow.FromDomain(rule));
    }

    public async Task UpdateAsync(QualificationRule rule)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE qualification_rules SET effective_end = @EffectiveEnd WHERE id = @Id",
            new { Id = rule.Id.ToString(), rule.EffectiveEnd });
    }

    public async Task DeleteAsync(Guid id)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync("DELETE FROM qualification_rules WHERE id = @Id", new { Id = id.ToString() });
    }

    private sealed class QualificationRuleRow
    {
        public string Id { get; set; } = string.Empty;
        public string ScopeType { get; set; } = string.Empty;
        public string? ScopeRef { get; set; }
        public int Version { get; set; }
        public string Conditions { get; set; } = string.Empty;
        public DateTimeOffset EffectiveStart { get; set; }
        public DateTimeOffset? EffectiveEnd { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }

        public QualificationRule ToDomain() => QualificationRule.Rehydrate(
            Guid.Parse(Id), Enum.Parse<QualificationScopeType>(ScopeType), ScopeRef, Version,
            JsonSerializer.Deserialize<QualificationConditions>(Conditions, JsonOptions)!,
            EffectiveStart, EffectiveEnd, CreatedBy, CreatedAt);

        public static object FromDomain(QualificationRule rule) => new
        {
            Id = rule.Id.ToString(),
            ScopeType = rule.ScopeType.ToString(),
            rule.ScopeRef,
            rule.Version,
            Conditions = JsonSerializer.Serialize(rule.Conditions, JsonOptions),
            rule.EffectiveStart,
            rule.EffectiveEnd,
            rule.CreatedBy,
            rule.CreatedAt,
        };
    }
}
