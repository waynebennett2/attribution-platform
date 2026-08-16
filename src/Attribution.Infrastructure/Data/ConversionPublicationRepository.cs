using System.Text.Json;
using Attribution.Domain.Publication;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class ConversionPublicationRepository : RepositoryBase, IConversionPublicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConversionPublicationRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<ConversionPublication?> GetActiveForCallAsync(Guid callId, PublicationDestination destination)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ConversionPublicationRow>(
            """
            SELECT cp.* FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            WHERE qr.call_id = @CallId AND cp.destination = @Destination AND cp.status <> 'Retracted'
            """,
            new { CallId = callId.ToString(), Destination = destination.ToString() });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<PublicationWorkItem>> GetRetryableAsync(int maxAttempts, int limit)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<ConversionPublicationRow, WorkItemJoinColumns, PublicationWorkItem>(
            """
            SELECT cp.*, c.id AS CallId, c.started_at AS CallStartedAt,
                   s.gclid AS Gclid, s.gbraid AS Gbraid, s.wbraid AS Wbraid, s.ga4_client_id AS Ga4ClientId
            FROM conversion_publications cp
            JOIN qualification_results qr ON qr.id = cp.qualification_result_id
            JOIN calls c ON c.id = qr.call_id
            JOIN attributions a ON a.id = qr.attribution_id
            LEFT JOIN sessions s ON s.id = a.session_id
            WHERE cp.status = 'Pending' OR (cp.status = 'Failed' AND cp.attempt_count < @MaxAttempts)
            ORDER BY cp.attempt_count ASC
            LIMIT @Limit
            """,
            (publicationRow, join) => new PublicationWorkItem(
                publicationRow.ToDomain(), Guid.Parse(join.CallId), join.CallStartedAt,
                join.Gclid, join.Gbraid, join.Wbraid, join.Ga4ClientId),
            new { MaxAttempts = maxAttempts, Limit = limit },
            splitOn: "CallId");
        return rows.ToList();
    }

    private sealed class WorkItemJoinColumns
    {
        public string CallId { get; set; } = string.Empty;
        public DateTimeOffset CallStartedAt { get; set; }
        public string? Gclid { get; set; }
        public string? Gbraid { get; set; }
        public string? Wbraid { get; set; }
        public string? Ga4ClientId { get; set; }
    }

    public async Task AddAsync(ConversionPublication publication)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO conversion_publications
                (id, qualification_result_id, destination, idempotency_key, status, skipped_reason,
                 attempt_count, external_id, last_error, correction, sent_at, corrected_at)
            VALUES
                (@Id, @QualificationResultId, @Destination, @IdempotencyKey, @Status, @SkippedReason,
                 @AttemptCount, @ExternalId, @LastError, @Correction, @SentAt, @CorrectedAt)
            """,
            ConversionPublicationRow.FromDomain(publication));
    }

    public async Task UpdateAsync(ConversionPublication publication)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE conversion_publications SET
                status = @Status, attempt_count = @AttemptCount, external_id = @ExternalId,
                last_error = @LastError, correction = @Correction, sent_at = @SentAt, corrected_at = @CorrectedAt
            WHERE id = @Id
            """,
            ConversionPublicationRow.FromDomain(publication));
    }

    private sealed class ConversionPublicationRow
    {
        public string Id { get; set; } = string.Empty;
        public string QualificationResultId { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? SkippedReason { get; set; }
        public int AttemptCount { get; set; }
        public string? ExternalId { get; set; }
        public string? LastError { get; set; }
        public string? Correction { get; set; }
        public DateTimeOffset? SentAt { get; set; }
        public DateTimeOffset? CorrectedAt { get; set; }

        public ConversionPublication ToDomain() => ConversionPublication.Rehydrate(
            Guid.Parse(Id), Guid.Parse(QualificationResultId), Enum.Parse<PublicationDestination>(Destination),
            IdempotencyKey, Enum.Parse<PublicationStatus>(Status), SkippedReason, AttemptCount, ExternalId, LastError,
            Correction is null ? null : JsonSerializer.Deserialize<PublicationCorrection>(Correction, JsonOptions),
            SentAt, CorrectedAt);

        public static object FromDomain(ConversionPublication publication) => new
        {
            Id = publication.Id.ToString(),
            QualificationResultId = publication.QualificationResultId.ToString(),
            Destination = publication.Destination.ToString(),
            publication.IdempotencyKey,
            Status = publication.Status.ToString(),
            publication.SkippedReason,
            publication.AttemptCount,
            publication.ExternalId,
            publication.LastError,
            Correction = publication.Correction is null ? null : JsonSerializer.Serialize(publication.Correction, JsonOptions),
            publication.SentAt,
            publication.CorrectedAt,
        };
    }
}
