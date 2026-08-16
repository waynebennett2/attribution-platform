using Attribution.Application.Administration;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class RetentionRepository : RepositoryBase, IRetentionRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public RetentionRepository(IDbConnectionFactory connectionFactory, IUnitOfWork unitOfWork) : base(connectionFactory)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<Guid>> GetVisitorIdsEligibleForDeIdentificationAsync(DateTimeOffset cutoff)
    {
        using var connection = OpenConnection();
        var ids = await connection.QueryAsync<string>(
            "SELECT id FROM visitors WHERE first_seen_at < @Cutoff AND de_identified_at IS NULL", new { Cutoff = cutoff });
        return ids.Select(Guid.Parse).ToList();
    }

    // Visitor and its Sessions are hard-deleted (nulled), not surrogated — research.md §10:
    // neither carries the evidence-chain requirement FR-019 places on Call/Attribution, so
    // there is nothing that needs to stay joinable once the tracking values themselves are gone.
    public async Task DeIdentifyVisitorAsync(Guid visitorId, DateTimeOffset now)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE visitors SET de_identified_at = @Now WHERE id = @Id AND de_identified_at IS NULL",
            new { Id = visitorId.ToString(), Now = now });
        await connection.ExecuteAsync(
            """
            UPDATE sessions SET
                landing_page = NULL, referrer = NULL, utm_source = NULL, utm_medium = NULL, utm_campaign = NULL,
                utm_term = NULL, utm_content = NULL, gclid = NULL, gbraid = NULL, wbraid = NULL, ga4_client_id = NULL,
                de_identified_at = @Now
            WHERE visitor_id = @VisitorId AND de_identified_at IS NULL
            """,
            new { VisitorId = visitorId.ToString(), Now = now });
    }

    public async Task<IReadOnlyList<(Guid Id, string? CallerId)>> GetCallsEligibleForDeIdentificationAsync(DateTimeOffset cutoff)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<(string Id, string? CallerId)>(
            "SELECT id, caller_id FROM calls WHERE started_at < @Cutoff AND de_identified_at IS NULL", new { Cutoff = cutoff });
        return rows.Select(r => (Guid.Parse(r.Id), r.CallerId)).ToList();
    }

    public async Task<bool> HasOpenReviewCaseAsync(Guid callId)
    {
        using var connection = OpenConnection();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM review_cases WHERE call_id = @CallId AND status = 'Open'", new { CallId = callId.ToString() });
        return count > 0;
    }

    public async Task DeIdentifyCallAsync(Guid callId, string? surrogateCallerId, DateTimeOffset now)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE calls SET caller_id = COALESCE(@SurrogateCallerId, caller_id), de_identified_at = @Now WHERE id = @Id",
            new { Id = callId.ToString(), SurrogateCallerId = surrogateCallerId, Now = now });
    }

    public async Task<IReadOnlyList<(Guid Id, string ExternalId)>> GetPublicationsEligibleForDeIdentificationAsync(DateTimeOffset cutoff)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<(string Id, string ExternalId)>(
            """
            SELECT cp.id, cp.external_id
            FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            JOIN calls c ON c.id = qr.call_id
            WHERE c.started_at < @Cutoff AND cp.de_identified_at IS NULL AND cp.external_id IS NOT NULL
            """,
            new { Cutoff = cutoff });
        return rows.Select(r => (Guid.Parse(r.Id), r.ExternalId)).ToList();
    }

    public async Task DeIdentifyPublicationAsync(Guid publicationId, string surrogateExternalId, DateTimeOffset now)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            "UPDATE conversion_publications SET external_id = @SurrogateExternalId, de_identified_at = @Now WHERE id = @Id",
            new { Id = publicationId.ToString(), SurrogateExternalId = surrogateExternalId, Now = now });
    }

    public async Task<IReadOnlyList<Guid>> GetCallsEligibleForPurgeAsync(DateTimeOffset cutoff)
    {
        using var connection = OpenConnection();
        var ids = await connection.QueryAsync<string>("SELECT id FROM calls WHERE started_at < @Cutoff", new { Cutoff = cutoff });
        return ids.Select(Guid.Parse).ToList();
    }

    // FK-forced deletion order (children before parents); wrapped in one transaction per
    // call so a mid-cascade failure never leaves an orphaned partial delete. The caller
    // (RetentionService) has already checked HasOpenReviewCaseAsync before reaching here.
    public async Task PurgeCallAsync(Guid callId) => await _unitOfWork.ExecuteAsync(async (connection, transaction) =>
    {
        var id = callId.ToString();
        await connection.ExecuteAsync(
            """
            DELETE cp FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            WHERE qr.call_id = @Id
            """,
            new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM qualification_results WHERE call_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM review_cases WHERE call_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM attributions WHERE call_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM call_legs WHERE call_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM calls WHERE id = @Id", new { Id = id }, transaction);
    });

    public async Task PurgeAuditLogOlderThanAsync(DateTimeOffset cutoff)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync("DELETE FROM audit_entries WHERE occurred_at < @Cutoff", new { Cutoff = cutoff });
    }

    public async Task<IReadOnlyList<(Guid Id, string? CallerId)>> GetCallsForVisitorAsync(Guid visitorId)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<(string Id, string? CallerId)>(
            """
            SELECT DISTINCT c.id, c.caller_id
            FROM calls c
            JOIN attributions a ON a.call_id = c.id
            JOIN sessions s ON s.id = a.session_id
            WHERE s.visitor_id = @VisitorId
            """,
            new { VisitorId = visitorId.ToString() });
        return rows.Select(r => (Guid.Parse(r.Id), r.CallerId)).ToList();
    }
}
