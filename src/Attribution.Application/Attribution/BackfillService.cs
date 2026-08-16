using Attribution.Application.Ingestion;
using Attribution.Domain.Calls;

namespace Attribution.Application.Attribution;

// FR-042: operator-specified period replay/backfill. Reuses IngestionService's exact
// upsert pipeline — the same idempotency guarantee that protects live ingestion protects
// a backfill covering an already-ingested range — but always via PollRangeAsync, whose
// page never carries a NextCheckpointPosition, so a backfill can never disturb the live
// checkpoint and is safe to run alongside live ingestion.
public sealed class BackfillService
{
    private readonly IAnalytics8x8Client _client;
    private readonly IngestionService _ingestionService;

    public BackfillService(IAnalytics8x8Client client, IngestionService ingestionService)
    {
        _client = client;
        _ingestionService = ingestionService;
    }

    public async Task RunAsync(string feed, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var page = await _client.PollRangeAsync(from, to, cancellationToken);
        await _ingestionService.ProcessPageAsync(feed, page, now);
    }
}
