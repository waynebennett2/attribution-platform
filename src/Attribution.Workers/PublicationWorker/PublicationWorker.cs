using Attribution.Domain.Publication;

namespace Attribution.Workers.PublicationWorker;

// FR-025-FR-028: drains the outbox to Google Ads/GA4, retrying transient failures with
// backoff (the fixed poll interval itself — a row that just failed simply waits for the
// next tick rather than being retried immediately) up to MaxAttempts, after which it stops
// being picked up (Acceptance Scenario 5: a permanent rejection is surfaced, not retried
// indefinitely — MarkRejected is what actually stops it; MarkFailed alone remains
// retryable up to the cap).
public sealed class PublicationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PublicationWorker> _logger;

    public PublicationWorker(IServiceScopeFactory scopeFactory, ILogger<PublicationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        _logger.LogInformation("PublicationWorker started with poll interval {Interval}", PollInterval);

        do
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PublicationWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversionPublicationRepository>();
        var googleAdsClient = scope.ServiceProvider.GetRequiredService<IGoogleAdsClient>();
        var ga4Client = scope.ServiceProvider.GetRequiredService<IGa4Client>();

        var items = await repository.GetRetryableAsync(MaxAttempts, BatchSize);
        foreach (var item in items)
        {
            await AttemptAsync(item, repository, googleAdsClient, ga4Client, cancellationToken);
        }

        if (items.Count > 0)
        {
            _logger.LogInformation("PublicationWorker attempted {Count} outbox rows", items.Count);
        }
    }

    private async Task AttemptAsync(
        PublicationWorkItem item, IConversionPublicationRepository repository,
        IGoogleAdsClient googleAdsClient, IGa4Client ga4Client, CancellationToken cancellationToken)
    {
        var publication = item.Publication;
        try
        {
            if (publication.Destination == PublicationDestination.GoogleAds)
            {
                var conversion = new GoogleAdsConversion(
                    item.Gclid, item.Gbraid, item.Wbraid, item.CallStartedAt, ConversionActionId: "call_qualified");
                var externalId = await googleAdsClient.UploadConversionAsync(conversion, cancellationToken);
                publication.MarkSent(externalId, DateTimeOffset.UtcNow);
            }
            else
            {
                var evt = new Ga4Event(item.Ga4ClientId!, "call_qualified", item.CallStartedAt);
                await ga4Client.SendEventAsync(evt, cancellationToken);
                publication.MarkSent(externalId: null, DateTimeOffset.UtcNow);
            }

            await repository.UpdateAsync(publication);
        }
        catch (Exception ex)
        {
            // FR-028: every attempt's outcome is recorded, including the failure reason —
            // a transient failure stays retryable (Acceptance Scenario 3) up to MaxAttempts,
            // matching the loop's own selection criteria in GetRetryableAsync.
            publication.MarkFailed(ex.Message);
            await repository.UpdateAsync(publication);
            _logger.LogWarning(
                ex, "Publication attempt failed for {Destination} (attempt {Attempt})",
                publication.Destination, publication.AttemptCount);
        }
    }
}
