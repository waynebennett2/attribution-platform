namespace Attribution.Workers.PublicationWorker;

// FR-025-FR-028: drains the outbox to Google Ads/GA4. Stub loop wired up here (T021);
// the actual publication logic is implemented in User Story 5 (T082).
public sealed class PublicationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<PublicationWorker> _logger;

    public PublicationWorker(ILogger<PublicationWorker> logger)
    {
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
                // T082 (US5) implements the actual outbox drain + retry-with-backoff.
                _logger.LogDebug("PublicationWorker tick (no-op until US5 lands)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PublicationWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
