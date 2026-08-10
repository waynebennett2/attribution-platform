namespace Attribution.Workers.IngestionWorker;

// FR-016: polls Analytics for 8x8 Work for Call Detail Records and Call Legs on a
// configurable cadence (default hourly). Stub loop wired up here (T021); the actual
// polling/attribution logic is implemented in User Story 2 (T054).
public sealed class IngestionWorker : BackgroundService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromHours(1);

    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(ILogger<IngestionWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(DefaultPollInterval);
        _logger.LogInformation("IngestionWorker started with poll interval {Interval}", DefaultPollInterval);

        do
        {
            try
            {
                // T054 (US2) implements the actual 8x8 poll + idempotent upsert + checkpoint advance.
                _logger.LogDebug("IngestionWorker tick (no-op until US2 lands)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IngestionWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
