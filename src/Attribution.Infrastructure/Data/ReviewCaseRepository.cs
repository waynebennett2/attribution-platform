using Attribution.Domain.Audit;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class ReviewCaseRepository : RepositoryBase, IReviewCaseRepository
{
    public ReviewCaseRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<ReviewCase?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ReviewCaseRow>(
            "SELECT * FROM review_cases WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<ReviewCase>> GetOpenAsync()
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<ReviewCaseRow>(
            "SELECT * FROM review_cases WHERE status = 'Open' ORDER BY opened_at ASC");
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task AddAsync(ReviewCase reviewCase)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO review_cases
                (id, call_id, attribution_id, status, opened_at, age_alert_raised_at, resolved_by, resolved_at, resolution)
            VALUES
                (@Id, @CallId, @AttributionId, @Status, @OpenedAt, @AgeAlertRaisedAt, @ResolvedBy, @ResolvedAt, @Resolution)
            """,
            RowFromDomain(reviewCase));
    }

    public async Task UpdateAsync(ReviewCase reviewCase)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE review_cases SET
                status = @Status, age_alert_raised_at = @AgeAlertRaisedAt,
                resolved_by = @ResolvedBy, resolved_at = @ResolvedAt, resolution = @Resolution
            WHERE id = @Id
            """,
            RowFromDomain(reviewCase));
    }

    private static object RowFromDomain(ReviewCase reviewCase) => new
    {
        Id = reviewCase.Id.ToString(),
        CallId = reviewCase.CallId.ToString(),
        AttributionId = reviewCase.AttributionId.ToString(),
        Status = reviewCase.Status.ToString(),
        reviewCase.OpenedAt,
        reviewCase.AgeAlertRaisedAt,
        reviewCase.ResolvedBy,
        reviewCase.ResolvedAt,
        reviewCase.Resolution,
    };

    private sealed class ReviewCaseRow
    {
        public string Id { get; set; } = string.Empty;
        public string CallId { get; set; } = string.Empty;
        public string AttributionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? AgeAlertRaisedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public string? Resolution { get; set; }

        public ReviewCase ToDomain() => ReviewCase.Rehydrate(
            Guid.Parse(Id), Guid.Parse(CallId), Guid.Parse(AttributionId), Enum.Parse<ReviewCaseStatus>(Status),
            OpenedAt, AgeAlertRaisedAt, ResolvedBy, ResolvedAt, Resolution);
    }
}
