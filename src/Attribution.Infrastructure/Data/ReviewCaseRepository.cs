using Attribution.Domain.Audit;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class ReviewCaseRepository : RepositoryBase, IReviewCaseRepository
{
    public ReviewCaseRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

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
            new
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
            });
    }
}
