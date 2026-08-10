namespace Attribution.Workers.RetentionWorker;

// FR-040: tiered purge/de-identification (14/25-month thresholds, 7-year audit log) and
// FR-039 erasure-request completion within 30 days (SC-019). Stub loop wired up here
// (T021); the actual retention logic is implemented in Polish (T100), test-first per
// SC-014's integration test (T122).
public sealed class RetentionWorker : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromHours(24);

    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(ILogger<RetentionWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        _logger.LogInformation("RetentionWorker started with evaluation interval {Interval}", EvaluationInterval);

        do
        {
            try
            {
                // T100 (Polish) implements the actual purge/de-identify/erasure logic.
                _logger.LogDebug("RetentionWorker tick (no-op until Polish phase lands)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
